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
        Console.WriteLine($"Starting Container App deployment: {name}");
        Console.Out.Flush();

        var projectDir = GetProjectDirectory(projectDirectory);
        Console.WriteLine($"Found project directory: {projectDir.FullName}");
        Console.Out.Flush();

        // Validate Dockerfile exists
        var dockerfilePath = Path.Combine(projectDir.FullName, "Dockerfile");
        if (!File.Exists(dockerfilePath))
        {
            throw new FileNotFoundException($"Dockerfile not found in project directory: {projectDir.FullName}");
        }
        Console.WriteLine($"Dockerfile found: {dockerfilePath}");
        Console.Out.Flush();

        var resourceGroup = await CreateResourceGroup(name);
        Console.WriteLine($"Resource group '{resourceGroup.Name}' created successfully.");
        Console.Out.Flush();

        // Create Azure Container Registry
        string acrName = $"{name.ToLower().Replace("-", "")}acr";
        if (acrName.Length > 50) acrName = acrName[..50];
        
        Console.WriteLine($"Creating Azure Container Registry '{acrName}'...");
        Console.Out.Flush();

        var acrData = new ContainerRegistryData(_location, new ContainerRegistrySku(ContainerRegistrySkuName.Basic))
        {
            IsAdminUserEnabled = true
        };
        var acr = await resourceGroup.Resource.GetContainerRegistries()
            .CreateOrUpdateAsync(WaitUntil.Completed, acrName, acrData);
        
        Console.WriteLine($"Container Registry '{acr.Value.Data.Name}' created successfully.");
        Console.WriteLine($"ACR Login Server: {acr.Value.Data.LoginServer}");
        Console.Out.Flush();

        // Get ACR credentials
        Console.WriteLine("Retrieving ACR credentials...");
        Console.Out.Flush();
        var credentials = await acr.Value.GetCredentialsAsync();
        string acrLoginServer = acr.Value.Data.LoginServer;
        string acrUsername = credentials.Value.Username;
        string acrPassword = credentials.Value.Passwords.First().Value;
        Console.WriteLine($"ACR credentials retrieved. Username: {acrUsername}");
        Console.Out.Flush();

        // Build and push Docker image
        string imageName = $"{acrLoginServer}/{name.ToLower()}:latest";
        Console.WriteLine($"Building and pushing Docker image: {imageName}");
        Console.Out.Flush();
        await BuildAndPushDockerImage(projectDir, acrLoginServer, acrUsername, acrPassword, name.ToLower());
        Console.WriteLine("Docker image built and pushed successfully.");
        Console.Out.Flush();

        // Create Container Apps Environment
        string environmentName = $"{name}-env";
        Console.WriteLine($"Creating Container Apps Environment '{environmentName}'...");
        Console.Out.Flush();
        var environmentData = new ContainerAppManagedEnvironmentData(_location);
        var environment = await resourceGroup.Resource.GetContainerAppManagedEnvironments()
            .CreateOrUpdateAsync(WaitUntil.Completed, environmentName, environmentData);

        Console.WriteLine($"Container Apps Environment '{environment.Value.Data.Name}' created successfully.");
        Console.Out.Flush();

        // Build environment variables for container
        var containerEnvVars = new List<ContainerAppEnvironmentVariable>
        {
            new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
            new() { Name = "ASPNETCORE_URLS", Value = "http://+:8080" }
        };
        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                containerEnvVars.Add(new ContainerAppEnvironmentVariable { Name = kvp.Key, Value = kvp.Value });
            }
        }
        Console.WriteLine($"Configured {containerEnvVars.Count} environment variables.");
        Console.Out.Flush();

        // Create container with environment variables
        var container = new ContainerAppContainer
        {
            Name = name.ToLower(),
            Image = imageName,
            Resources = new AppContainerResources
            {
                Cpu = 0.5,
                Memory = "1Gi"
            }
        };
        foreach (var envVar in containerEnvVars)
        {
            container.Env.Add(envVar);
        }

        // Create Container App
        Console.WriteLine($"Creating Container App '{name}'...");
        Console.Out.Flush();
        var containerAppData = new ContainerAppData(_location)
        {
            ManagedEnvironmentId = environment.Value.Id,
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
                        Server = acrLoginServer,
                        Username = acrUsername,
                        PasswordSecretRef = "acr-password"
                    }
                },
                Secrets =
                {
                    new ContainerAppWritableSecret { Name = "acr-password", Value = acrPassword }
                }
            },
            Template = new ContainerAppTemplate
            {
                Containers = { container },
                Scale = new ContainerAppScale
                {
                    MinReplicas = 1,
                    MaxReplicas = 1
                }
            }
        };

        var containerApp = await resourceGroup.Resource.GetContainerApps()
            .CreateOrUpdateAsync(WaitUntil.Completed, name, containerAppData);

        Console.WriteLine($"Container App '{containerApp.Value.Data.Name}' created successfully.");
        Console.Out.Flush();

        // Wait for the Container App to be ready
        string fqdn = containerApp.Value.Data.Configuration.Ingress.Fqdn;
        Console.WriteLine($"Container App FQDN: {fqdn}");
        Console.Out.Flush();
        await WaitForContainerAppToBeReady(fqdn);

        Console.WriteLine($"Container App deployment complete: https://{fqdn}");
        Console.Out.Flush();

        return new AzureContainerApp(containerApp.Value.Data.Name, fqdn, resourceGroup.Resource.Data.Name, this);
    }

    private static async Task BuildAndPushDockerImage(
        DirectoryInfo projectDir,
        string acrLoginServer,
        string acrUsername,
        string acrPassword,
        string imageName)
    {
        // Verify Docker is available
        Console.WriteLine("Verifying Docker is available...");
        Console.Out.Flush();
        var versionProcess = new Process
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
        versionProcess.Start();
        string versionOutput = await versionProcess.StandardOutput.ReadToEndAsync();
        await versionProcess.WaitForExitAsync();
        if (versionProcess.ExitCode != 0)
        {
            string versionError = await versionProcess.StandardError.ReadToEndAsync();
            throw new Exception($"Docker is not available or not running: {versionError}");
        }
        Console.WriteLine($"Docker is available. OS: {versionOutput.Trim()}");
        Console.Out.Flush();

        // Docker login
        Console.WriteLine($"Logging into ACR {acrLoginServer}...");
        Console.Out.Flush();
        var loginProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"login {acrLoginServer} -u {acrUsername} -p {acrPassword}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        loginProcess.Start();
        string loginOutput = await loginProcess.StandardOutput.ReadToEndAsync();
        string loginError = await loginProcess.StandardError.ReadToEndAsync();
        await loginProcess.WaitForExitAsync();
        if (loginProcess.ExitCode != 0)
        {
            throw new Exception($"Docker login failed: {loginError}");
        }
        Console.WriteLine($"Docker login successful: {loginOutput.Trim()}");
        Console.Out.Flush();

        // Docker build
        string imageTag = $"{acrLoginServer}/{imageName}:latest";
        Console.WriteLine($"Building Docker image {imageTag}...");
        Console.WriteLine($"Working directory: {projectDir.FullName}");
        Console.Out.Flush();
        
        var buildProcess = new Process
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
        buildProcess.Start();
        
        // Read output asynchronously to prevent blocking
        var buildOutputTask = buildProcess.StandardOutput.ReadToEndAsync();
        var buildErrorTask = buildProcess.StandardError.ReadToEndAsync();
        await buildProcess.WaitForExitAsync();
        
        string buildOutput = await buildOutputTask;
        string buildError = await buildErrorTask;
        
        Console.WriteLine("Docker build output:");
        Console.WriteLine(buildOutput);
        Console.Out.Flush();
        
        if (buildProcess.ExitCode != 0)
        {
            Console.WriteLine("Docker build error:");
            Console.WriteLine(buildError);
            Console.Out.Flush();
            throw new Exception($"Docker build failed with exit code {buildProcess.ExitCode}: {buildError}");
        }
        Console.WriteLine("Docker build completed successfully.");
        Console.Out.Flush();

        // Docker push
        Console.WriteLine($"Pushing Docker image {imageTag}...");
        Console.Out.Flush();
        var pushProcess = new Process
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
        pushProcess.Start();
        
        // Read output asynchronously
        var pushOutputTask = pushProcess.StandardOutput.ReadToEndAsync();
        var pushErrorTask = pushProcess.StandardError.ReadToEndAsync();
        await pushProcess.WaitForExitAsync();
        
        string pushOutput = await pushOutputTask;
        string pushError = await pushErrorTask;
        
        Console.WriteLine("Docker push output:");
        Console.WriteLine(pushOutput);
        Console.Out.Flush();
        
        if (pushProcess.ExitCode != 0)
        {
            Console.WriteLine("Docker push error:");
            Console.WriteLine(pushError);
            Console.Out.Flush();
            throw new Exception($"Docker push failed with exit code {pushProcess.ExitCode}: {pushError}");
        }

        Console.WriteLine($"Docker image {imageTag} pushed successfully.");
        Console.Out.Flush();
    }

    private static async Task WaitForContainerAppToBeReady(string fqdn, int timeoutMinutes = 10, int intervalSeconds = 30)
    {
        string appUrl = $"https://{fqdn}";
        var timeout = TimeSpan.FromMinutes(timeoutMinutes);
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine($"Waiting for Container App to be ready at: {appUrl}");
        Console.Out.Flush();

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                Console.WriteLine($"Checking Container App health... (elapsed: {stopwatch.Elapsed:mm\\:ss})");
                Console.Out.Flush();
                var response = await httpClient.GetAsync(appUrl);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Container App is ready! Status: {response.StatusCode}");
                    Console.Out.Flush();
                    return;
                }

                Console.WriteLine($"Container App not ready yet. Status: {response.StatusCode}");
                Console.Out.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Container App not ready yet. Error: {ex.Message}");
                Console.Out.Flush();
            }

            if (stopwatch.Elapsed + interval < timeout)
            {
                Console.WriteLine($"Waiting {intervalSeconds} seconds before next check...");
                Console.Out.Flush();
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
