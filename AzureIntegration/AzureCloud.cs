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
    private readonly ArmInfrastructure? _armInfrastructure;
    private readonly IInfrastructure _infrastructure;
    private readonly IDockerProcessRunner _dockerProcessRunner;
    public AzureLocation Location { get; }

    // Static lock and flag for MSBuild registration to ensure thread safety
    private static readonly object MsBuildLock = new();
    private static bool _msBuildRegistered;

    // Static lock for MSBuild operations to prevent concurrent builds
    private static readonly object MsBuildOperationLock = new();

    public AzureCloud(AzureLocation? location = null) : this(new DefaultAzureCredential(), location)
    {
    }

    public AzureCloud(TokenCredential credentials, AzureLocation? location = null)
    {
        Location = location ?? AzureLocation.WestEurope;
        _armInfrastructure = new ArmInfrastructure(credentials, Location);
        _infrastructure = _armInfrastructure;
        _dockerProcessRunner = new ProcessDockerRunner();
    }

    internal AzureCloud(IInfrastructure infrastructure, IDockerProcessRunner dockerProcessRunner, AzureLocation? location = null)
    {
        _infrastructure = infrastructure;
        _dockerProcessRunner = dockerProcessRunner;
        Location = location ?? AzureLocation.WestEurope;
    }

    private ArmInfrastructure RequireArm()
        => _armInfrastructure ?? throw new InvalidOperationException("This operation requires Azure ARM credentials.");

    internal Task<SubscriptionResource> GetSubscriptionAsync()
        => RequireArm().GetSubscriptionAsync();

    public async Task<ResourceGroup> CreateResourceGroup(string name)
    {
        var arm = RequireArm();
        await arm.CreateResourceGroup(name);
        var resource = await arm.GetResourceGroupResource(name);
        return new ResourceGroup(resource, this);
    }

    public async Task DeleteResourceGroup(string name)
    {
        RequireArm();
        await _infrastructure.DeleteResourceGroup(name);
    }

    public async Task<bool> ResourceGroupExists(string name)
    {
        return await RequireArm().ResourceGroupExists(name);
    }

    public async Task<AzureFunction> DeployAzureFunction(
        string projectDirectory, string name, Dictionary<string, string>? environmentVariables = null)
    {
        string zipFilePath = CreateDeploymentZipFile(projectDirectory, name);
        var resourceGroup = await CreateResourceGroup(name);
        
        try
        {
            var storageAccount = await resourceGroup.CreateStorageAccount(name);
            var appServicePlan = await resourceGroup.CreateAppServicePlanForFunctionApp(name);
            var applicationInsights = await resourceGroup.CreateApplicationInsights(name);

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
                                  new() { Name = "WEBSITE_CONTENTSHARE", Value = name.ToLower() },
                                  new() { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = applicationInsights.ConnectionString },
                                  new() { Name = "ApplicationInsightsAgent_EXTENSION_VERSION", Value = "~3" }
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

            await DeployZipFile(zipFilePath, name);

            Console.WriteLine($"Function App '{functionApp.Value.Data.Name}' created successfully.");

            return new AzureFunction(functionApp.Value, applicationInsights, resourceGroup.Resource.Data.Name, this);
        }
        catch (Exception)
        {
            await DeleteResourceGroup(name);
            throw;
        }
    }

    public async Task<AzureWebApp> DeployAppService(string projectDirectory, string name, Dictionary<string, string>? environmentVariables = null)
    {
        string zipFilePath = CreateDeploymentZipFile(projectDirectory, name);
        var resourceGroup = await CreateResourceGroup(name);
        
        try
        {
            // Create App Service Plan specifically for web apps using the dedicated method
            string appServicePlanName = $"{name.ToLower()}-plan-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            var appServicePlan = await resourceGroup.CreateAppServicePlanForWebApp(appServicePlanName);

            var appSettings = new List<AppServiceNameValuePair>
                              {
                                  new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
                              };
            appSettings = AddEnvironmentVariablesToAppSettings(appSettings, environmentVariables);
            var webAppData = new WebSiteData(resourceGroup.Resource.Data.Location)
                             {
                                 AppServicePlanId = appServicePlan.Id,
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
        catch (Exception)
        {
            await DeleteResourceGroup(name);
            throw;
        }
    }

    public Task<ManagedIdentity> CreateUserAssignedIdentity(string resourceGroupName, string identityName)
        => ManagedIdentity.CreateAsync(this, resourceGroupName, identityName);

    public Task<AzureKeyVault> CreateKeyVaultWithSecret(
        string resourceGroupName,
        string secretName,
        string secretValue,
        Guid identityPrincipalId)
        => AzureKeyVault.CreateAsync(this, resourceGroupName, secretName, secretValue, identityPrincipalId);

    public async Task<AzureContainerApp> DeployContainerApp(
        string projectDirectory,
        string name,
        Dictionary<string, string>? environmentVariables = null,
        string? workspaceRoot = null,
        Dictionary<string, string>? dockerBuildArguments = null,
        string? managedIdentityResourceId = null)
    {
        DeploymentLogger.Start($"Starting Container App deployment: {name}");

        (var buildContext, var projectDir) = ProjectPathResolver.GetBuildContextPaths(projectDirectory, workspaceRoot);

        await _infrastructure.CreateResourceGroup(name);

        try
        {
            var registry = await _infrastructure.CreateContainerRegistry(name, ArmInfrastructure.SanitizeAcrName(name));

            await VerifyDockerAvailable(_dockerProcessRunner);
            await DockerLogin(registry.LoginServer, registry.Username, registry.Password, _dockerProcessRunner);

            string imageTag = $"{registry.LoginServer}/{name.ToLower()}:latest";
            string dockerfilePath = Path.GetRelativePath(buildContext.FullName, Path.Combine(projectDir.FullName, "Dockerfile"))
                .Replace('\\', '/');
            await DockerBuild(buildContext, imageTag, dockerfilePath, dockerBuildArguments, _dockerProcessRunner);
            await DockerPush(imageTag, _dockerProcessRunner);

            var applicationInsights = await _infrastructure.CreateApplicationInsights(name, name);
            var environmentId = await _infrastructure.CreateContainerAppsEnvironment(name, name);
            var containerAppInfo = await _infrastructure.CreateContainerApp(name, environmentId, registry, name, imageTag, environmentVariables, applicationInsights, managedIdentityResourceId);

            var containerApp = new AzureContainerApp(containerAppInfo.Name, containerAppInfo.Fqdn, containerAppInfo.ResourceGroupName, this, containerAppInfo.ApplicationInsights);

            DeploymentLogger.Log($"Deployment complete: {containerApp.Url}");
            return containerApp;
        }
        catch (Exception ex)
        {
            DeploymentLogger.LogError($"Deployment failed: {ex.Message}. Cleaning up resources...");
            await _infrastructure.DeleteResourceGroup(name);
            throw;
        }
    }

    private static async Task VerifyDockerAvailable(IDockerProcessRunner runner)
    {
        DeploymentLogger.Log("Verifying Docker availability...");
        var result = await runner.RunAsync("version --format '{{.Server.Os}}'");
        if (result.ExitCode != 0)
            throw new Exception($"Docker is not available or not running: {result.StdErr}");
        DeploymentLogger.Log($"Docker available (OS: {result.StdOut.Trim()})");
    }

    private static async Task DockerLogin(string server, string username, string password, IDockerProcessRunner runner)
    {
        DeploymentLogger.Log($"Logging into ACR {server}...");
        var result = await runner.RunAsync($"login {server} -u {username} --password-stdin", stdinInput: password);
        if (result.ExitCode != 0)
            throw new Exception($"Docker login failed: {result.StdErr}");
        DeploymentLogger.Log("Docker login successful");
    }

    private static async Task DockerBuild(DirectoryInfo buildContext, string imageTag, string dockerfilePath, Dictionary<string, string>? dockerBuildArguments, IDockerProcessRunner runner)
    {
        DeploymentLogger.Log($"Building Docker image {imageTag}...");
        string buildArgsString = dockerBuildArguments is { Count: > 0 }
            ? string.Join(" ", dockerBuildArguments.Select(a => $"--build-arg {a.Key}={a.Value}"))
            : string.Empty;
        var result = await runner.RunAsync($"build --platform linux/amd64 -t {imageTag} -f {dockerfilePath} {buildArgsString} .", workingDirectory: buildContext.FullName);
        if (result.ExitCode != 0)
        {
            DeploymentLogger.LogError($"Docker build failed: {result.StdErr}");
            throw new Exception($"Docker build failed with exit code {result.ExitCode}: {result.StdErr}");
        }
        DeploymentLogger.Log("Docker build completed");
    }

    private static async Task DockerPush(string imageTag, IDockerProcessRunner runner)
    {
        DeploymentLogger.Log("Pushing Docker image...");
        var result = await runner.RunAsync($"push {imageTag}");
        if (result.ExitCode != 0)
        {
            DeploymentLogger.LogError($"Docker push failed: {result.StdErr}");
            throw new Exception($"Docker push failed with exit code {result.ExitCode}: {result.StdErr}");
        }
        DeploymentLogger.Log("Docker image pushed successfully");
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
        var accessToken = await RequireArm().GetCredential().GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), CancellationToken.None);
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
                throw new InvalidOperationException($"Project build failed for {projectFile.FullName}. Check build output above for details.");
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
        var arm = RequireArm();
        var subscription = await arm.GetSubscriptionAsync();
        var resourceGroups = new List<ResourceGroup>();
        await foreach (var resourceGroup in subscription.GetResourceGroups().GetAllAsync())
        {
            resourceGroups.Add(new ResourceGroup(resourceGroup, this));
        }

        return resourceGroups;
    }

    public ArmClient GetArmClient()
    {
        return RequireArm().GetArmClient();
    }

    public TokenCredential GetCredential()
    {
        return RequireArm().GetCredential();
    }
}
