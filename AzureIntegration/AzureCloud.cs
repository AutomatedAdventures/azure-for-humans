using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.ContainerRegistry;
using Azure.ResourceManager.ContainerRegistry.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.Build.Logging;

namespace AzureIntegration;

public class AzureCloud
{
    private readonly TokenCredential _azureCredentials;
    private readonly ArmClient _armClient;
    private SubscriptionResource _subscription;
    // Changed region from WestEurope to NorthEurope to avoid capacity issues
    private readonly AzureLocation _location = AzureLocation.NorthEurope;

    // Static lock and flag for MSBuild registration to ensure thread safety
    private static readonly object MsBuildLock = new();
    private static bool _msBuildRegistered;

    // Static lock for MSBuild operations to prevent concurrent builds
    private static readonly object MsBuildOperationLock = new();

    public AzureCloud() : this(new DefaultAzureCredential())
    {
    }

    public AzureCloud(TokenCredential credentials)
    {
        _azureCredentials = credentials;
        _armClient = new ArmClient(_azureCredentials);
        _subscription = null!;
    }

    private async Task<SubscriptionResource> GetSubscriptionAsync()
    {
        if (_subscription == null)
        {
            try
            {
                _subscription = await _armClient.GetDefaultSubscriptionAsync();
            }
            catch (AuthenticationFailedException)
            {
                throw new AuthenticationFailedException("Invalid credentials provided. Please check your client ID, client secret, and tenant ID.");
            }
            catch (Exception ex) when (ex.InnerException is AuthenticationFailedException)
            {
                throw new AuthenticationFailedException("Invalid credentials provided. Please check your client ID, client secret, and tenant ID.");
            }
        }
        return _subscription;
    }

    public async Task<ResourceGroup> CreateResourceGroup(string name)
    {
        var subscription = await GetSubscriptionAsync();
        var resourceGroupData = new ResourceGroupData(_location);
        var operationResult = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, name, resourceGroupData);
        return new ResourceGroup(operationResult.Value);
    }

    public async Task DeleteResourceGroup(string name)
    {
        var subscription = await GetSubscriptionAsync();
        var resourceGroup = (await subscription.GetResourceGroups().GetAsync(name)).Value;
        await resourceGroup.DeleteAsync(WaitUntil.Completed);
    }

    public async Task<AzureFunction> DeployAzureFunction(
        string projectDirectory, string name, Dictionary<string, string>? environmentVariables = null)
    {
        string zipFilePath = CreateDeploymentZipFile(projectDirectory, name);
        var resourceGroup = await CreateResourceGroup(name);
        var storageAccount = await resourceGroup.CreateStorageAccount(name);
        var appServicePlan = await resourceGroup.CreateAppServicePlan(name);

        //TODO: Create application insights

        var appSettings = new List<AppServiceNameValuePair>
                          {
                              new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                              new() { Name = "AzureWebJobsStorage", Value = storageAccount.ConnectionString },
                              new() { Name = "WEBSITE_RUN_FROM_PACKAGE", Value = "1" },
                              new() { Name = "FUNCTIONS_EXTENSION_VERSION", Value = "~4" },
                              new() { Name = "FUNCTIONS_WORKER_RUNTIME", Value = "dotnet-isolated" },
                              new() { Name = "SCM_DO_BUILD_DURING_DEPLOYMENT", Value = "0" },
                              new() { Name = "WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED", Value = "1" },
                              new() { Name = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING", Value = storageAccount.ConnectionString },
                              new() { Name = "WEBSITE_CONTENTSHARE", Value = name.ToLower() }
                          };
        appSettings = AddEnvironmentVariablesToAppSettings(appSettings, environmentVariables);
        var functionAppData = new WebSiteData(resourceGroup.Resource.Data.Location)
                              {
                                  AppServicePlanId = appServicePlan.Id,
                                  Kind = "functionapp,linux",
                                  SiteConfig = new SiteConfigProperties
                                               {
                                                   AppSettings = appSettings,
                                                   LinuxFxVersion = "DOTNET-ISOLATED|8.0",
                                               }
                              };

        var functionApp = await resourceGroup.Resource.GetWebSites().CreateOrUpdateAsync(
                              WaitUntil.Completed, name, functionAppData);

        //TODO: link application insights

        await DeployZipFile(zipFilePath, name);

        Console.WriteLine($"Function App '{functionApp.Value.Data.Name}' created successfully.");

        return new AzureFunction(functionApp.Value, resourceGroup.Resource.Data.Name, this);
    }

    public async Task<AzureWebApp> DeployAppService(string projectDirectory, string name, Dictionary<string, string>? environmentVariables = null)
    {
        string zipFilePath = CreateDeploymentZipFile(projectDirectory, name);
        var resourceGroup = await CreateResourceGroup(name);
        // Ensure App Service Plan name is unique per deployment
        string appServicePlanName = $"{name.ToLower()}-plan-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var appServicePlanData = new AppServicePlanData(resourceGroup.Resource.Data.Location)
                                 {
                                     Sku = new AppServiceSkuDescription { Name = "B1", Tier = "Basic" },
                                     Kind = "app,linux",
                                     IsReserved = true // Use Linux
                                 };
        var appServicePlan = await resourceGroup.Resource.GetAppServicePlans().CreateOrUpdateAsync(
                                 WaitUntil.Completed, appServicePlanName, appServicePlanData);

        var appSettings = new List<AppServiceNameValuePair>
                          {
                              new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
                          };
        appSettings = AddEnvironmentVariablesToAppSettings(appSettings, environmentVariables);
        var webAppData = new WebSiteData(resourceGroup.Resource.Data.Location)
                         {
                             AppServicePlanId = appServicePlan.Value.Id,
                             Kind = "app,linux",
                             SiteConfig = new SiteConfigProperties
                                          {
                                              AppSettings = appSettings,
                                              LinuxFxVersion = "DOTNETCORE|8.0"
                                          }
                         };
        var webApp = await resourceGroup.Resource.GetWebSites().CreateOrUpdateAsync(
                         WaitUntil.Completed, name, webAppData);

        await DeployZipFile(zipFilePath, name);

        // Wait for the App Service to be ready to receive HTTP requests
        await WaitForAppServiceToBeReady(webApp.Value.Data.DefaultHostName);

        return new AzureWebApp(webApp.Value, resourceGroup.Resource.Data.Name, this);
    }

    public async Task<AzureContainerApp> DeployContainerApp(
        string projectDirectory,
        string name,
        Dictionary<string, string>? environmentVariables = null)
    {
        DeploymentLogger.Start($"Starting Container App deployment: {name}");

        var projectDir = ValidateProjectDirectory(projectDirectory);
        var resourceGroup = await CreateResourceGroup(name);
        var acr = await CreateContainerRegistry(resourceGroup, name);
        
        string imageName = await BuildAndPushImage(projectDir, acr, name);
        
        var environment = await CreateContainerAppsEnvironment(resourceGroup, name);
        var containerApp = await CreateContainerApp(resourceGroup, environment, acr, name, imageName, environmentVariables);

        DeploymentLogger.Log($"Deployment complete: {containerApp.Url}");
        return containerApp;
    }

    private static DirectoryInfo ValidateProjectDirectory(string projectDirectory)
    {
        var projectDir = GetProjectDirectory(projectDirectory);
        DeploymentLogger.Log($"Project directory: {projectDir.FullName}");

        var dockerfilePath = Path.Combine(projectDir.FullName, "Dockerfile");
        if (!File.Exists(dockerfilePath))
        {
            throw new FileNotFoundException($"Dockerfile not found in: {projectDir.FullName}");
        }
        return projectDir;
    }

    private async Task<ContainerRegistryResource> CreateContainerRegistry(ResourceGroup resourceGroup, string name)
    {
        string acrName = SanitizeAcrName(name);
        DeploymentLogger.Log($"Creating Container Registry '{acrName}'...");

        var acrData = new ContainerRegistryData(_location, new ContainerRegistrySku(ContainerRegistrySkuName.Basic))
        {
            IsAdminUserEnabled = true
        };
        var acr = await resourceGroup.Resource.GetContainerRegistries()
            .CreateOrUpdateAsync(WaitUntil.Completed, acrName, acrData);
        
        DeploymentLogger.Log($"Container Registry created: {acr.Value.Data.LoginServer}");
        return acr.Value;
    }

    private static string SanitizeAcrName(string name)
    {
        string acrName = $"{name.ToLower().Replace("-", "")}acr";
        return acrName.Length > 50 ? acrName[..50] : acrName;
    }

    private static async Task<string> BuildAndPushImage(DirectoryInfo projectDir, ContainerRegistryResource acr, string name)
    {
        var credentials = await acr.GetCredentialsAsync();
        string loginServer = acr.Data.LoginServer;
        string username = credentials.Value.Username;
        string password = credentials.Value.Passwords.First().Value;

        await BuildAndPushDockerImage(projectDir, loginServer, username, password, name.ToLower());
        return $"{loginServer}/{name.ToLower()}:latest";
    }

    private static async Task BuildAndPushDockerImage(
        DirectoryInfo projectDir,
        string acrLoginServer,
        string acrUsername,
        string acrPassword,
        string imageName)
    {
        await VerifyDockerAvailable();
        await DockerLogin(acrLoginServer, acrUsername, acrPassword);
        
        string imageTag = $"{acrLoginServer}/{imageName}:latest";
        await DockerBuild(projectDir, imageTag);
        await DockerPush(imageTag);
    }

    private static async Task VerifyDockerAvailable()
    {
        DeploymentLogger.Log("Verifying Docker availability...");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version --format '{{.Server.Os}}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Docker is not available or not running: {error}");
        }
        DeploymentLogger.Log($"Docker available (OS: {output.Trim()})");
    }

    private static async Task DockerLogin(string server, string username, string password)
    {
        DeploymentLogger.Log($"Logging into ACR {server}...");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"login {server} -u {username} -p {password}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0)
        {
            throw new Exception($"Docker login failed: {error}");
        }
        DeploymentLogger.Log("Docker login successful");
    }

    private static async Task DockerBuild(DirectoryInfo projectDir, string imageTag)
    {
        DeploymentLogger.Log($"Building Docker image {imageTag}...");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"build --platform linux/amd64 -t {imageTag} .",
                WorkingDirectory = projectDir.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string error = await errorTask;
        
        if (process.ExitCode != 0)
        {
            DeploymentLogger.LogError($"Docker build failed: {error}");
            throw new Exception($"Docker build failed with exit code {process.ExitCode}: {error}");
        }
        DeploymentLogger.Log("Docker build completed");
    }

    private static async Task DockerPush(string imageTag)
    {
        DeploymentLogger.Log("Pushing Docker image...");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"push {imageTag}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string error = await errorTask;
        
        if (process.ExitCode != 0)
        {
            DeploymentLogger.LogError($"Docker push failed: {error}");
            throw new Exception($"Docker push failed with exit code {process.ExitCode}: {error}");
        }
        DeploymentLogger.Log("Docker image pushed successfully");
    }

    private static ContainerAppContainer BuildContainer(
        string name, 
        string imageName, 
        Dictionary<string, string>? environmentVariables)
    {
        var container = new ContainerAppContainer
        {
            Name = name.ToLower(),
            Image = imageName,
            Resources = new AppContainerResources { Cpu = 0.5, Memory = "1Gi" }
        };

        container.Env.Add(new ContainerAppEnvironmentVariable 
        { 
            Name = "DEPLOYMENT_DATE", 
            Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") 
        });
        container.Env.Add(new ContainerAppEnvironmentVariable 
        { 
            Name = "ASPNETCORE_URLS", 
            Value = "http://+:8080" 
        });

        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                container.Env.Add(new ContainerAppEnvironmentVariable { Name = kvp.Key, Value = kvp.Value });
            }
        }

        return container;
    }

    private async Task<ContainerAppManagedEnvironmentResource> CreateContainerAppsEnvironment(ResourceGroup resourceGroup, string name)
    {
        string environmentName = $"{name}-env";
        DeploymentLogger.Log($"Creating Container Apps Environment '{environmentName}'...");
        
        var environmentData = new ContainerAppManagedEnvironmentData(_location);
        var environment = await resourceGroup.Resource.GetContainerAppManagedEnvironments()
            .CreateOrUpdateAsync(WaitUntil.Completed, environmentName, environmentData);

        DeploymentLogger.Log("Container Apps Environment created");
        return environment.Value;
    }

    private async Task<AzureContainerApp> CreateContainerApp(
        ResourceGroup resourceGroup,
        ContainerAppManagedEnvironmentResource environment,
        ContainerRegistryResource acr,
        string name,
        string imageName,
        Dictionary<string, string>? environmentVariables)
    {
        DeploymentLogger.Log($"Creating Container App '{name}'...");

        var credentials = await acr.GetCredentialsAsync();
        var container = BuildContainer(name, imageName, environmentVariables);
        
        var containerAppData = new ContainerAppData(_location)
        {
            ManagedEnvironmentId = environment.Id,
            Configuration = new ContainerAppConfiguration
            {
                Ingress = new ContainerAppIngressConfiguration
                {
                    External = true,
                    TargetPort = 8080,
                    Transport = ContainerAppIngressTransportMethod.Auto
                },
                Registries =
                {
                    new ContainerAppRegistryCredentials
                    {
                        Server = acr.Data.LoginServer,
                        Username = credentials.Value.Username,
                        PasswordSecretRef = "acr-password"
                    }
                },
                Secrets =
                {
                    new ContainerAppWritableSecret 
                    { 
                        Name = "acr-password", 
                        Value = credentials.Value.Passwords.First().Value 
                    }
                }
            },
            Template = new ContainerAppTemplate
            {
                Containers = { container },
                Scale = new ContainerAppScale { MinReplicas = 1, MaxReplicas = 1 }
            }
        };

        var containerApp = await resourceGroup.Resource.GetContainerApps()
            .CreateOrUpdateAsync(WaitUntil.Completed, name, containerAppData);

        string fqdn = containerApp.Value.Data.Configuration.Ingress.Fqdn;
        DeploymentLogger.Log("Container App created, waiting for readiness...");
        
        await WaitForContainerAppToBeReady(fqdn);

        return new AzureContainerApp(containerApp.Value.Data.Name, fqdn, resourceGroup.Resource.Data.Name, this);
    }

    private static async Task WaitForContainerAppToBeReady(string fqdn, int timeoutMinutes = 10, int intervalSeconds = 30)
    {
        string appUrl = $"https://{fqdn}";
        var timeout = TimeSpan.FromMinutes(timeoutMinutes);
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var stopwatch = Stopwatch.StartNew();

        DeploymentLogger.Log($"Waiting for Container App at {appUrl}...");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await httpClient.GetAsync(appUrl);

                if (response.IsSuccessStatusCode)
                {
                    DeploymentLogger.Log($"Container App ready (Status: {response.StatusCode})");
                    return;
                }

                DeploymentLogger.Log($"Not ready yet (Status: {response.StatusCode})");
            }
            catch (Exception ex)
            {
                DeploymentLogger.Log($"Not ready yet ({ex.Message})");
            }

            if (stopwatch.Elapsed + interval < timeout)
            {
                await Task.Delay(interval);
            }
        }

        throw new TimeoutException($"Container App at {appUrl} did not become ready within {timeoutMinutes} minutes");
    }

    private static DirectoryInfo GetProjectDirectory(string projectDirectory)
    {
        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var projectDir = currentDirectory.Parent!.Parent!.Parent!.Parent!
            .GetDirectories(projectDirectory, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (projectDir == null)
        {
            throw new DirectoryNotFoundException($"Project directory not found: {projectDirectory}");
        }
        return projectDir;
    }

    private async Task DeployZipFile(string zipFilePath, string serviceName)
    {
        var httpClient = new HttpClient
                         {
                             Timeout = TimeSpan.FromMinutes(10)
                         };
        string url = $"https://{serviceName.ToLower()}.scm.azurewebsites.net/api/zipdeploy";
        await using var fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var accessToken = await _azureCredentials.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), CancellationToken.None);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

        Console.WriteLine($"Deploying zip file to {serviceName}...");
        var response = await httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Deployment failed for {serviceName}: {response.StatusCode} {errorContent}");
        }

        Console.WriteLine($"Zip file deployed successfully to {serviceName}");
    }

    private static List<AppServiceNameValuePair> AddEnvironmentVariablesToAppSettings(
        List<AppServiceNameValuePair> baseSettings,
        Dictionary<string, string>? environmentVariables)
    {
        var appSettings = new List<AppServiceNameValuePair>(baseSettings);

        if (environmentVariables == null)
        {
            return appSettings;
        }

        foreach (var kvp in environmentVariables)
        {
            appSettings.Add(new AppServiceNameValuePair { Name = kvp.Key, Value = kvp.Value });
        }

        return appSettings;
    }

    private static async Task WaitForAppServiceToBeReady(string hostName, int timeoutMinutes = 10, int intervalSeconds = 30)
    {
        string appUrl = $"https://{hostName}";
        var timeout = TimeSpan.FromMinutes(timeoutMinutes);
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine($"Waiting for App Service to be ready at: {appUrl}");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                Console.WriteLine($"Checking App Service health... (elapsed: {stopwatch.Elapsed:mm\\:ss})");
                var response = await httpClient.GetAsync(appUrl);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"App Service is ready! Status: {response.StatusCode}");
                    return;
                }

                Console.WriteLine($"App Service not ready yet. Status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"App Service not ready yet. Error: {ex.Message}");
            }

            if (stopwatch.Elapsed + interval < timeout)
            {
                Console.WriteLine($"Waiting {intervalSeconds} seconds before next check...");
                await Task.Delay(interval);
            }
        }

        throw new TimeoutException($"App Service at {appUrl} did not become ready within {timeoutMinutes} minutes");
    }

    //TODO: use project name instead of project directory
    private static string CreateDeploymentZipFile(string projectDirectory, string serviceName)
    {
        var publishDirectory = PublishProject(projectDirectory, serviceName);

        string destinationZipFile = Path.Combine(
            Path.GetTempPath(), "AzureFunctionPublish", serviceName, "zip", $"{Path.GetFileName(projectDirectory)}.zip");

        string? destinationZipDirectory = Path.GetDirectoryName(destinationZipFile);
        if (!string.IsNullOrEmpty(destinationZipDirectory) && !Directory.Exists(destinationZipDirectory))
        {
            Directory.CreateDirectory(destinationZipDirectory);
        }

        if (File.Exists(destinationZipFile))
        {
            File.Delete(destinationZipFile);
        }

        if (!publishDirectory.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {publishDirectory}");
        }

        // Create the ZIP file
        ZipFile.CreateFromDirectory(publishDirectory.FullName, destinationZipFile);

        Console.WriteLine("Zip file created: {0}", destinationZipFile);

        return destinationZipFile;
    }

    //TODO: refactor to use DirectoryInfo instead of string
    //TODO: get project paths from solution or from root directory
    private static DirectoryInfo PublishProject(string projectDirectory, string serviceName)
    {
        var projectFile = GetProjectFile(projectDirectory);
        string publishDirectory = DefinePublishDirectory(serviceName);

        lock (MsBuildLock)
        {
            if (!_msBuildRegistered)
            {
                MSBuildLocator.RegisterDefaults();
                _msBuildRegistered = true;
            }
        }

        PublishProjectUsingMsBuild(projectFile, publishDirectory);
        return new DirectoryInfo(publishDirectory);
    }

    private static string DefinePublishDirectory(string serviceName)
    {
        string publishDirectory = Path.Combine(Path.GetTempPath(), "AzureFunctionPublish", serviceName, "publish");
        if (Directory.Exists(publishDirectory))
        {
            Directory.Delete(publishDirectory, true);
        }

        Directory.CreateDirectory(publishDirectory);
        Console.WriteLine("Publish directory: {0}", publishDirectory);
        return publishDirectory;
    }

    private static void PublishProjectUsingMsBuild(FileInfo projectFile, string publishDirectory)
    {
        Console.WriteLine($"Publishing {projectFile.Name} to {publishDirectory}");

        lock (MsBuildOperationLock)
        {
            var globalProperties = new Dictionary<string, string>
                                   {
                                       { "Configuration", "Release" },
                                       { "OutputPath", publishDirectory },
                                       { "MSBuildSDKsPath", @"/usr/share/dotnet/sdk" } // Adjust the path to your .NET SDK location
                                   };

            var projectCollection = new ProjectCollection(globalProperties);
            var logger = new ConsoleLogger(LoggerVerbosity.Normal);
            projectCollection.RegisterLogger(logger);
            var project = projectCollection.LoadProject(projectFile.FullName);
            bool buildResult = project.Build("Publish");

            if (!buildResult)
            {
                throw new InvalidOperationException($"Project publish failed for {projectFile.FullName}. Check build output above for details.");
            }
        }

        Console.WriteLine("Project published successfully to: {0}", publishDirectory);
    }

    private static FileInfo GetProjectFile(string projectDirectory)
    {
        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var projectFile = currentDirectory.Parent!.Parent!.Parent!.Parent!.GetFiles($"{projectDirectory}.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (projectFile == null)
        {
            throw new FileNotFoundException("Project file not found", projectDirectory);
        }

        return projectFile;
    }

    public async Task<IEnumerable<ResourceGroup>> GetResourceGroups()
    {
        var subscription = await GetSubscriptionAsync();
        var resourceGroups = new List<ResourceGroup>();
        await foreach (var resourceGroup in subscription.GetResourceGroups().GetAllAsync())
        {
            resourceGroups.Add(new ResourceGroup(resourceGroup));
        }

        return resourceGroups;
    }
}
