using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.Build.Logging;

namespace AzureIntegration;

public class AzureCloud
{
    private readonly DefaultAzureCredential _azureCredentials;

    private readonly SubscriptionResource _subscription;

    // Changed region from WestEurope to NorthEurope to avoid capacity issues
    private readonly AzureLocation _location = AzureLocation.NorthEurope;

    // Static lock and flag for MSBuild registration to ensure thread safety
    private static readonly object MsBuildLock = new();
    private static bool _msBuildRegistered;

    // Static lock for MSBuild operations to prevent concurrent builds
    private static readonly object MsBuildOperationLock = new();

    public AzureCloud()
    {
        _azureCredentials = new DefaultAzureCredential();
        var armClient = new ArmClient(_azureCredentials);
        _subscription = armClient.GetDefaultSubscriptionAsync().Result;
    }

    public async Task<ResourceGroup> CreateResourceGroup(string name)
    {
        var resourceGroupData = new ResourceGroupData(_location);
        var operationResult = await _subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, name, resourceGroupData);
        return new ResourceGroup(operationResult.Value);
    }

    public async Task DeleteResourceGroup(string name)
    {
        var resourceGroup = (await _subscription.GetResourceGroups().GetAsync(name)).Value;
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
        var accessToken = await _azureCredentials.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]));
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
        var resourceGroups = new List<ResourceGroup>();
        await foreach (var resourceGroup in _subscription.GetResourceGroups().GetAllAsync())
        {
            resourceGroups.Add(new ResourceGroup(resourceGroup));
        }

        return resourceGroups;
    }
}
