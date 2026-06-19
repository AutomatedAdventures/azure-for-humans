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
    private readonly IArmClient _arm;
    private readonly IDocker _docker;
    private readonly IAppService _appService;
    private readonly IFunctionService _functionService;
    private readonly IContainerAppService _containerAppService;
    private readonly IManagedIdentityService _managedIdentityService;
    private readonly IKeyVaultService _keyVaultService;
    private readonly IReadOnlyList<AzureLocation> _locations;
    public AzureLocation Location { get; }

    internal IDocker Docker => _docker;
    internal IManagedIdentityService ManagedIdentityService => _managedIdentityService;
    internal IKeyVaultService KeyVaultService => _keyVaultService;

    // Static lock and flag for MSBuild registration to ensure thread safety
    private static readonly object MsBuildLock = new();
    private static bool _msBuildRegistered;

    // Static lock for MSBuild operations to prevent concurrent builds
    private static readonly object MsBuildOperationLock = new();

    public AzureCloud(AzureLocation? location = null) : this(new DefaultAzureCredential(), location)
    {
    }

    public AzureCloud(IEnumerable<AzureLocation> locations) : this(new DefaultAzureCredential())
    {
        var configuredLocations = locations.ToArray();
        Location = configuredLocations.First();
        _locations = configuredLocations;
    }

    public AzureCloud(IArmClient armClient, IEnumerable<AzureLocation> locations)
        : this(armClient, new ProcessDocker(), locations)
    {
    }

    public AzureCloud(IArmClient armClient, IDocker docker, IEnumerable<AzureLocation> locations)
        : this(armClient, new FailingAppService(), new FailingFunctionService(), new FailingContainerAppService(), docker, locations)
    {
    }

    public AzureCloud(IArmClient armClient, IAppService appService, IDocker docker, IEnumerable<AzureLocation> locations)
        : this(armClient, appService, new FailingFunctionService(), new FailingContainerAppService(), docker, locations)
    {
    }

    public AzureCloud(IArmClient armClient, IAppService appService, IFunctionService functionService, IDocker docker, IEnumerable<AzureLocation> locations)
        : this(armClient, appService, functionService, new FailingContainerAppService(), docker, locations)
    {
    }

    public AzureCloud(IArmClient armClient, IAppService appService, IFunctionService functionService, IContainerAppService containerAppService, IDocker docker, IEnumerable<AzureLocation> locations)
        : this(armClient, appService, functionService, containerAppService, new FailingManagedIdentityService(), docker, locations)
    {
    }

    public AzureCloud(IArmClient armClient, IAppService appService, IFunctionService functionService, IContainerAppService containerAppService, IManagedIdentityService managedIdentityService, IDocker docker, IEnumerable<AzureLocation> locations)
        : this(armClient, appService, functionService, containerAppService, managedIdentityService, new FailingKeyVaultService(), docker, locations)
    {
    }

    public AzureCloud(IArmClient armClient, IAppService appService, IFunctionService functionService, IContainerAppService containerAppService, IManagedIdentityService managedIdentityService, IKeyVaultService keyVaultService, IDocker docker, IEnumerable<AzureLocation> locations)
    {
        _arm = armClient;
        _appService = appService;
        _functionService = functionService;
        _containerAppService = containerAppService;
        _managedIdentityService = managedIdentityService;
        _keyVaultService = keyVaultService;
        _docker = docker;
        var configuredLocations = locations.ToArray();
        Location = configuredLocations.First();
        _locations = configuredLocations;
        _azureCredentials = null!;
        _armClient = null!;
    }

    public AzureCloud(TokenCredential credentials, AzureLocation? location = null)
    {
        _azureCredentials = credentials;
        _armClient = new ArmClient(_azureCredentials);
        _arm = new ArmClientAdapter(_armClient);
        _docker = new ProcessDocker();
        _appService = new RealAppService(_azureCredentials);
        _functionService = new RealFunctionService(_azureCredentials);
        _containerAppService = new RealContainerAppService();
        _managedIdentityService = new RealManagedIdentityService();
        _keyVaultService = new RealKeyVaultService(_azureCredentials);
        Location = location ?? AzureLocation.WestEurope;
        _locations = new[] { Location };
    }


    public Task<ResourceGroup> CreateResourceGroup(string name) =>
        CreateResourceGroupIn(name, Location);

    public async Task DeleteResourceGroup(string name)
    {
        var subscription = await _arm.GetDefaultSubscriptionAsync();
        var resourceGroup = await subscription.GetResourceGroups().GetAsync(name);
        await resourceGroup.DeleteAsync(WaitUntil.Completed);
    }

    public async Task<bool> ResourceGroupExists(string name)
    {
        var subscription = await _arm.GetDefaultSubscriptionAsync();
        return await subscription.GetResourceGroups().ExistsAsync(name);
    }

    public async Task<bool> ContainerRegistryExists(string resourceId)
    {
        return await _arm.GetContainerRegistryResource(new ResourceIdentifier(resourceId)).Exists();
    }

    public async Task<AzureFunction> DeployAzureFunction(
        string projectDirectory, string name, Dictionary<string, string>? environmentVariables = null)
    {
        string zipFilePath = CreateDeploymentZipFile(projectDirectory, name);
        var resourceGroup = await CreateResourceGroup(name);

        try
        {
            var deployment = await _functionService.Deploy(resourceGroup, name, projectDirectory, environmentVariables, zipFilePath);
            return new AzureFunction(name, deployment.Host, deployment.Logs, resourceGroup.Name, this);
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
            string hostName = await _appService.Deploy(resourceGroup, name, projectDirectory, environmentVariables, zipFilePath);
            return new AzureWebApp(name, hostName, resourceGroup.Name, this);
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

    public Task<ContainerRegistry> CreateContainerRegistry(string resourceGroupName, string registryName)
        => ContainerRegistry.CreateAsync(this, resourceGroupName, registryName);

    public async Task<AzureContainerApp> DeployContainerApp(
        string projectDirectory,
        string name,
        Dictionary<string, string>? environmentVariables = null,
        string? workspaceRoot = null,
        Dictionary<string, string>? dockerBuildArguments = null,
        string? managedIdentityResourceId = null,
        string? containerRegistryResourceId = null)
    {
        DeploymentLogger.Start($"Starting Container App deployment: {name}");

        (var buildContext, var projectDir) = ProjectPathResolver.GetBuildContextPaths(projectDirectory, workspaceRoot);

        RequestFailedException? lastCapacityError = null;
        foreach (var location in _locations)
        {
            var resourceGroup = await CreateResourceGroupIn(name, location);

            try
            {
                var acr = await ResolveContainerRegistry(resourceGroup, name, containerRegistryResourceId);

                string imageName = await BuildAndPushImage(projectDir, buildContext, acr, name, dockerBuildArguments);

                var deployment = await _containerAppService.Deploy(resourceGroup, name, imageName, acr, environmentVariables, managedIdentityResourceId);

                DeploymentLogger.Log($"Deployment complete: https://{deployment.Fqdn}");
                return new AzureContainerApp(name, deployment.Fqdn, resourceGroup.Name, this, deployment.Logs);
            }
            catch (RequestFailedException ex) when (ex.ErrorCode == "AKSCapacityHeavyUsage")
            {
                lastCapacityError = ex;
                DeploymentLogger.Log($"Region '{location}' has no capacity, trying the next configured region...");
                await DeleteResourceGroup(name);
            }
            catch (Exception ex)
            {
                DeploymentLogger.LogError($"Deployment failed: {ex.Message}. Cleaning up resources...");
                await DeleteResourceGroup(name);
                throw;
            }
        }

        throw lastCapacityError!;
    }

    private async Task<ResourceGroup> CreateResourceGroupIn(string name, AzureLocation location)
    {
        var subscription = await _arm.GetDefaultSubscriptionAsync();
        var createdResourceGroup = await subscription.GetResourceGroups()
            .CreateOrUpdateAsync(WaitUntil.Completed, name, new ResourceGroupData(location));
        return new ResourceGroup(createdResourceGroup.Concrete!, this) { SeamResource = createdResourceGroup };
    }

    private async Task<IContainerRegistryResource> CreateContainerRegistry(ResourceGroup resourceGroup, string name)
    {
        string acrName = SanitizeAcrName(name);
        DeploymentLogger.Log($"Creating Container Registry '{acrName}'...");

        var acrData = new ContainerRegistryData(Location, new ContainerRegistrySku(ContainerRegistrySkuName.Basic))
        {
            IsAdminUserEnabled = true
        };
        var acr = await resourceGroup.SeamResource!.GetContainerRegistries()
            .CreateOrUpdateAsync(WaitUntil.Completed, acrName, acrData);

        DeploymentLogger.Log($"Container Registry created: {acr.LoginServer}");
        return acr;
    }

    private async Task<IContainerRegistryResource> ResolveContainerRegistry(
        ResourceGroup resourceGroup,
        string name,
        string? containerRegistryResourceId)
    {
        if (string.IsNullOrWhiteSpace(containerRegistryResourceId))
        {
            return await CreateContainerRegistry(resourceGroup, name);
        }

        DeploymentLogger.Log($"Using existing Container Registry '{containerRegistryResourceId}'...");
        var existingRegistry = _arm.GetContainerRegistryResource(new ResourceIdentifier(containerRegistryResourceId));
        DeploymentLogger.Log($"Using existing Container Registry server: {existingRegistry.LoginServer}");
        return existingRegistry;
    }

    private static string SanitizeAcrName(string name)
    {
        string acrName = $"{name.ToLower().Replace("-", "")}acr";
        return acrName.Length > 50 ? acrName[..50] : acrName;
    }

    private async Task<string> BuildAndPushImage(DirectoryInfo projectDir, DirectoryInfo buildContext, IContainerRegistryResource acr, string name, Dictionary<string, string>? dockerBuildArguments)
    {
        var credentials = await acr.GetCredentialsAsync();
        string loginServer = acr.LoginServer;

        await BuildAndPushDockerImage(projectDir, buildContext, loginServer, credentials.Username, credentials.Password, name.ToLower(), dockerBuildArguments);
        return $"{loginServer}/{name.ToLower()}:latest";
    }

    private async Task BuildAndPushDockerImage(
        DirectoryInfo projectDir,
        DirectoryInfo buildContext,
        string acrLoginServer,
        string acrUsername,
        string acrPassword,
        string imageName,
        Dictionary<string, string>? dockerBuildArguments)
    {
        await _docker.Verify();
        await _docker.Login(acrLoginServer, acrUsername, acrPassword);

        string imageTag = $"{acrLoginServer}/{imageName}:latest";
        string dockerfilePath = Path.GetRelativePath(buildContext.FullName, Path.Combine(projectDir.FullName, "Dockerfile"))
            .Replace('\\', '/');
        await _docker.Build(buildContext, imageTag, dockerfilePath, dockerBuildArguments);
        await _docker.Push(imageTag);
    }

    internal static async Task DeployZipFile(TokenCredential credentials, string zipFilePath, string serviceName)
    {
        var httpClient = new HttpClient
                         {
                             Timeout = TimeSpan.FromMinutes(10)
                         };
        string url = $"https://{serviceName.ToLower()}.scm.azurewebsites.net/api/zipdeploy";
        await using var fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var accessToken = await credentials.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), CancellationToken.None);
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

    internal static List<AppServiceNameValuePair> AddEnvironmentVariablesToAppSettings(
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

    internal static async Task WaitForAppServiceToBeReady(string hostName, int timeoutMinutes = 10, int intervalSeconds = 30)
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
        var subscription = await _arm.GetDefaultSubscriptionAsync();
        var resourceGroups = new List<ResourceGroup>();
        foreach (var resourceGroup in await subscription.GetResourceGroups().GetAllAsync())
        {
            resourceGroups.Add(new ResourceGroup(resourceGroup.Concrete!, this) { SeamResource = resourceGroup });
        }

        return resourceGroups;
    }

    public ArmClient GetArmClient()
    {
        return _armClient;
    }

    public TokenCredential GetCredential()
    {
        return _azureCredentials;
    }
}
