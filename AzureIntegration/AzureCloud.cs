using System.IO.Compression;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using Microsoft.Build.Locator;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using System.Net.Http.Headers;

namespace AzureIntegration;

public class AzureCloud
{
    private readonly DefaultAzureCredential _azureCredentials;
    private readonly ArmClient _armClient;
    private readonly SubscriptionResource _subscription;
    // Changed region from WestEurope to NorthEurope to avoid capacity issues
    private readonly AzureLocation _location = AzureLocation.NorthEurope;

    public AzureCloud()
    {
        _azureCredentials = new DefaultAzureCredential();
        _armClient = new ArmClient(_azureCredentials);
        _subscription = _armClient.GetDefaultSubscriptionAsync().Result;
    }

    public async Task<ResourceGroup> CreateResourceGroup(string name)
    {
        var resourceGroupData = new ResourceGroupData(_location);
        var operationResult = await _subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, name, resourceGroupData);
        return new ResourceGroup(operationResult.Value);
    }

    public async Task DeleteResourceGroup(string name)
    {
        await _subscription.GetResourceGroups().Get(name).Value.DeleteAsync(WaitUntil.Completed);
    }

    public async Task<AzureFunction> DeployAzureFunction(string projectDirectory, string name, Dictionary<string, string>? environmentVariables = null)
    {
        var zipFilePath = CreateAzureFunctionZipFile(projectDirectory);
        var resourceGroup = await CreateResourceGroup(name);
        var storageAccount = await resourceGroup.CreateStorageAccount(name);
        var appServicePlan = await resourceGroup.CreateAppServicePlan(name);    
        
        //TODO: Create application insights

        var appSettings = new List<AppServiceNameValuePair>
        {
            new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
            new() { Name = "AzureWebJobsStorage", Value = storageAccount.ConnectionString },
            new() { Name = "WEBSITE_RUN_FROM_PACKAGE", Value = "1"},
            new() { Name = "FUNCTIONS_EXTENSION_VERSION", Value = "~4" },
            new() { Name = "FUNCTIONS_WORKER_RUNTIME", Value = "dotnet-isolated" },
            new() { Name = "SCM_DO_BUILD_DURING_DEPLOYMENT", Value = "0" },
            new() { Name = "WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED", Value = "1" },
            new() { Name = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING", Value = storageAccount.ConnectionString },
            new() { Name = "WEBSITE_CONTENTSHARE", Value = name.ToLower() }
        };
        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                appSettings.Add(new AppServiceNameValuePair { Name = kvp.Key, Value = kvp.Value });
            }
        }
        var functionAppData = new WebSiteData(resourceGroup.resourceGroup.Data.Location)
        {
            AppServicePlanId = appServicePlan.Id,
            Kind = "functionapp,linux",
            SiteConfig = new SiteConfigProperties
            {
                AppSettings = appSettings,
                LinuxFxVersion = "DOTNET-ISOLATED|8.0",
            }
        };

        var functionApp = await resourceGroup.resourceGroup.GetWebSites().CreateOrUpdateAsync(
            WaitUntil.Completed, name, functionAppData);

        //TODO: link application insights

        //TODO: make post to the azure function with the zip file

        var httpClient = new HttpClient();

        // URL del endpoint para subir el archivo ZIP tolower
        var url = $"https://{name.ToLower()}.scm.azurewebsites.net/api/zipdeploy";

        using var fileStream = new FileStream(zipFilePath, FileMode.Open);
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var accessToken = await _azureCredentials.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]));
        // Agregar encabezado de autorización
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

        Console.WriteLine("Subiendo el archivo ZIP...");
        var response = await httpClient.PostAsync(url, content);

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine("¡Archivo ZIP subido exitosamente!");
        }
        else
        {
            Console.WriteLine($"Error al subir el archivo: {response.StatusCode}");
            Console.WriteLine($"Detalles: {await response.Content.ReadAsStringAsync()}");
        }
        

        Console.WriteLine($"Function App '{functionApp.Value.Data.Name}' created successfully.");

        return new AzureFunction(functionApp.Value, resourceGroup.resourceGroup.Data.Name, this);
    }

    public async Task<AzureWebApp> DeployAppService(string projectDirectory, string name)
    {
        var zipFilePath = CreateAzureFunctionZipFile(projectDirectory);
        var resourceGroup = await CreateResourceGroup(name);
        // Ensure App Service Plan name is unique per deployment
        var appServicePlanName = $"{name.ToLower()}-plan-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var appServicePlanData = new AppServicePlanData(resourceGroup.resourceGroup.Data.Location)
        {
            Sku = new AppServiceSkuDescription { Name = "B1", Tier = "Basic" },
            Kind = "app,linux",
            IsReserved = true // Use Linux
        };
        var appServicePlan = await resourceGroup.resourceGroup.GetAppServicePlans().CreateOrUpdateAsync(
            WaitUntil.Completed, appServicePlanName, appServicePlanData);

        var appSettings = new List<AppServiceNameValuePair>
        {
            new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
        };
        var webAppData = new WebSiteData(resourceGroup.resourceGroup.Data.Location)
        {
            AppServicePlanId = appServicePlan.Value.Id,
            Kind = "app,linux",
            SiteConfig = new SiteConfigProperties
            {
                AppSettings = appSettings,
                LinuxFxVersion = "DOTNETCORE|8.0"
            }
        };
        var webApp = await resourceGroup.resourceGroup.GetWebSites().CreateOrUpdateAsync(
            WaitUntil.Completed, name, webAppData);

        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        var url = $"https://{name.ToLower()}.scm.azurewebsites.net/api/zipdeploy";
        using var fileStream = new FileStream(zipFilePath, FileMode.Open);
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var accessToken = await _azureCredentials.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
        var response = await httpClient.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"App Service deployment failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        // Wait for the App Service to be ready to receive HTTP requests
        await WaitForAppServiceToBeReady(webApp.Value.Data.DefaultHostName);

        return new AzureWebApp(webApp.Value, resourceGroup.resourceGroup.Data.Name, this);
    }

    private async Task WaitForAppServiceToBeReady(string hostName, int timeoutMinutes = 10, int intervalSeconds = 30)
    {
        var appUrl = $"https://{hostName}";
        var timeout = TimeSpan.FromMinutes(timeoutMinutes);
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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
    private string CreateAzureFunctionZipFile(string projectDirectory)
    {
        var publishDirectory = PublishProject(projectDirectory);

        var destinationZipFile = Path.Combine(Path.GetTempPath(), "AzureFunctionPublish", Path.GetFileName(projectDirectory), "zip", $"{Path.GetFileName(projectDirectory)}.zip");

        var destinationZipDirectory = Path.GetDirectoryName(destinationZipFile);
        if (!Directory.Exists(destinationZipDirectory))
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
    private DirectoryInfo PublishProject(string projectDirectory)
    {
        var projectFile = GetProjectFile(projectDirectory);
        var publishDirectory = DefinePublishDirectory(projectDirectory); 

        MSBuildLocator.RegisterDefaults();

        PublishProjectUsingMsBuild(projectFile, publishDirectory);
        return new DirectoryInfo(publishDirectory);
    }

    private string DefinePublishDirectory(string projectDirectory)
    {
        var publishDirectory = Path.Combine(Path.GetTempPath(), "AzureFunctionPublish", Path.GetFileName(projectDirectory), "publish");
        if (Directory.Exists(publishDirectory))
        {
            Directory.Delete(publishDirectory, true);
        }

        Directory.CreateDirectory(publishDirectory);
        Console.WriteLine("Publish directory: {0}", publishDirectory);
        return publishDirectory;
    }

    private void PublishProjectUsingMsBuild(FileInfo projectFile, string publishDirectory)
    {
        Console.WriteLine($"Publishing {projectFile.Name} to {publishDirectory}");
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
        var buildResult = project.Build("Publish");
        
        if (!buildResult)
        {
            throw new InvalidOperationException($"Project publish failed for {projectFile.FullName}. Check build output above for details.");
        }
        
        Console.WriteLine("Project published successfully to: {0}", publishDirectory);
    }

    public FileInfo GetProjectFile(string projectDirectory)
    {
        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var projectFile = currentDirectory!.Parent!.Parent!.Parent!.Parent!.GetFiles($"{projectDirectory}.csproj", SearchOption.AllDirectories).FirstOrDefault();
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