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
    //TODO: try with location spain central
    private readonly AzureLocation _location = AzureLocation.WestEurope;

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

    public async Task<AzureFunction> DeployAzureFunction(string projectDirectory, string name)
    {
        var zipFilePath = CreateAzureFunctionZipFile(projectDirectory);
        var resourceGroup = await CreateResourceGroup(name);
        var storageAccount = await resourceGroup.CreateStorageAccount(name);
        var appServicePlan = await resourceGroup.CreateAppServicePlan(name);    
        
        //TODO: Create application insights

        var functionAppData = new WebSiteData(resourceGroup.resourceGroup.Data.Location)
        {
            AppServicePlanId = appServicePlan.Id,
            Kind = "functionapp,linux",
            SiteConfig = new SiteConfigProperties
            {
            AppSettings =
            [
                new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                new() { Name = "AzureWebJobsStorage", Value = storageAccount.ConnectionString },
                new() { Name = "WEBSITE_RUN_FROM_PACKAGE", Value = "1"},
                new() { Name = "FUNCTIONS_EXTENSION_VERSION", Value = "~4" },
                new() { Name = "FUNCTIONS_WORKER_RUNTIME", Value = "dotnet-isolated" },
                new() { Name = "SCM_DO_BUILD_DURING_DEPLOYMENT", Value = "0" },
                new() { Name = "WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED", Value = "1" },
                new() { Name = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING", Value = storageAccount.ConnectionString },
                new() { Name = "WEBSITE_CONTENTSHARE", Value = name.ToLower() },
                //TODO: Add more environment variables from parameter
            ],
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

        return new AzureFunction(functionApp.Value);

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

        // PublishUsingMsBuild1(projectFile, publishDirectory);
        // PublishUsingMsBuild2(projectFile, publishDirectory);
        // PublishUsingMsBuild3(projectFile, publishDirectory);
        PublishUsingMsBuild4(projectFile, publishDirectory);
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

    private void PublishUsingMsBuild1(FileInfo projectFile, string publishDirectory)
    {
        var projectCollection = new ProjectCollection();
        var buildRequestData = new BuildRequestData(projectFile.FullName, new Dictionary<string, string>
        {
            { "Configuration", "Release" },
            { "OutputPath", publishDirectory }
        }, null, ["Publish"], null);

        var buildParameters = new BuildParameters(projectCollection)
        {
            Loggers = [new ConsoleLogger(LoggerVerbosity.Minimal)]
        };

        var buildResult = BuildManager.DefaultBuildManager.Build(buildParameters, buildRequestData);
        if (buildResult.OverallResult == BuildResultCode.Failure)
        {
            throw new InvalidOperationException("Project publish failed");
        }

        Console.WriteLine("Project published to: {0}", publishDirectory);
    }

    private void PublishUsingMsBuild2(FileInfo projectFile, string publishDirectory)
    {
        var project = ProjectRootElement.Open(projectFile.FullName);
        var projectInstance = new ProjectInstance(project);

        var publishTarget = projectInstance.Targets.FirstOrDefault(t => t.Value.Name == "Publish").Value;
        if (publishTarget == null)
        {
            throw new InvalidOperationException("Publish target not found in project file");
        }

        var buildRequestData = new BuildRequestData(projectInstance, ["Publish"]);
        var buildParameters = new BuildParameters(ProjectCollection.GlobalProjectCollection)
        {
            Loggers = [new ConsoleLogger(LoggerVerbosity.Minimal)]
        };

        var buildResult = BuildManager.DefaultBuildManager.Build(buildParameters, buildRequestData);
        if (buildResult.OverallResult == BuildResultCode.Failure)
        {
            throw new InvalidOperationException("Project publish failed");
        }

        Console.WriteLine("Project published to: {0}", publishDirectory);
    }

    private void PublishUsingMsBuild3(FileInfo projectFile, string publishDirectory)
    {
        var globalProperties = new Dictionary<string, string>
        {
            { "Configuration", "Release" },
            { "OutputPath", publishDirectory }
        };

        var projectCollection = new ProjectCollection();
        var buildRequestData = new BuildRequestData(projectFile.FullName, globalProperties, null, ["Publish"], null);

        var buildParameters = new BuildParameters(projectCollection)
        {
            Loggers = null // You can add custom loggers if needed
        };

        var buildResult = BuildManager.DefaultBuildManager.Build(buildParameters, buildRequestData);

        if (buildResult.OverallResult == BuildResultCode.Success)
        {
            Console.WriteLine("Build and publish succeeded!");
        }
        else
        {
            Console.WriteLine("Build and publish failed.");
        }
    }

    private void PublishUsingMsBuild4(FileInfo projectFile, string publishDirectory)
    {
            var globalProperties = new Dictionary<string, string>
            {
                { "Configuration", "Release" },
                { "OutputPath", publishDirectory },
                { "MSBuildSDKsPath", @"/usr/share/dotnet/sdk" } // Adjust the path to your .NET SDK location
            };

            var projectCollection = new ProjectCollection(globalProperties);
            projectCollection.RegisterLogger(new ConsoleLogger(LoggerVerbosity.Minimal));
            var project = projectCollection.LoadProject(projectFile.FullName);
            project.Build("Publish");
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

public record ResourceGroup(ResourceGroupResource resourceGroup)
{
    public string Name => resourceGroup.Data.Name;
    public async Task<StorageAccount> CreateStorageAccount(string name){
        var storageAccountName = $"{name.ToLower().Replace("-", "")}storage";
        if (storageAccountName.Length > 24)
        {
            storageAccountName = storageAccountName.Substring(0, 24);
        }
        var storageSku = new StorageSku(StorageSkuName.StandardLrs);
        var storageKind = StorageKind.StorageV2;
        var storageParameters = new StorageAccountCreateOrUpdateContent(storageSku, storageKind, location: resourceGroup.Data.Location);

        var storageAccount = await resourceGroup.GetStorageAccounts().CreateOrUpdateAsync(
            WaitUntil.Completed, storageAccountName, storageParameters);

        return new StorageAccount(storageAccount.Value);
    }

    public async Task<AppServicePlan> CreateAppServicePlan(string name){
        var appServicePlanData = new AppServicePlanData(resourceGroup.Data.Location)
        {
            Sku = new AppServiceSkuDescription { Name = "Y1",Tier = "Dynamic" },
            Kind = "FunctionApp",            
            IsReserved = true //Use linux
        };

        var appServicePlan = await resourceGroup.GetAppServicePlans().CreateOrUpdateAsync(
            WaitUntil.Completed, name, appServicePlanData);

        return new AppServicePlan(appServicePlan.Value);
    }
}

public class StorageAccount
{
    private readonly StorageAccountResource _storageAccount;
    private string _connectionString;

    public StorageAccount(StorageAccountResource storageAccount)
    {
        _storageAccount = storageAccount;
    }

    public string Name => _storageAccount.Data.Name;
    public string ConnectionString
    {
        get
        {
            return _connectionString ??= GetConnectionStringAsync();

        }
    }

    public string GetConnectionStringAsync()
    {
        var keys = _storageAccount.GetKeysAsync().ToBlockingEnumerable();
        var key = keys.First().Value;
        return $"DefaultEndpointsProtocol=https;AccountName={Name};AccountKey={key};EndpointSuffix=core.windows.net";
    }
    
}

public class AppServicePlan
{
    private readonly AppServicePlanResource _appServicePlan;

    public AppServicePlan(AppServicePlanResource appServicePlan)
    {
        _appServicePlan = appServicePlan;
    }

    public string Name => _appServicePlan.Data.Name;
    public ResourceIdentifier Id => _appServicePlan.Id;
}

public class AzureFunction
{
    private readonly WebSiteResource _functionApp;

    public AzureFunction(WebSiteResource functionApp)
    {
        _functionApp = functionApp;
    }

    public string Name => _functionApp.Data.Name;
    public string Url => _functionApp.Data.DefaultHostName;
}