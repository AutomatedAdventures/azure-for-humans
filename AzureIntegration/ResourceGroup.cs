using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.ApplicationInsights;
using Azure.ResourceManager.OperationalInsights;
using Azure.Monitor.Query;
using Azure;

namespace AzureIntegration;

public record ResourceGroup(ResourceGroupResource Resource, AzureCloud AzureCloud)
{
    private const int MaxStorageAccountNameLength = 24;
    
    public string Name => Resource.Data.Name;

    public async Task<StorageAccount> CreateStorageAccount(string name)
    {
        string storageAccountName = SanitizeStorageAccountName(name);
        
        var storageSku = new StorageSku(StorageSkuName.StandardLrs);
        var storageKind = StorageKind.StorageV2;
        var storageParameters = new StorageAccountCreateOrUpdateContent(storageSku, storageKind, location: Resource.Data.Location);

        var storageAccount = await Resource.GetStorageAccounts().CreateOrUpdateAsync(
            WaitUntil.Completed, storageAccountName, storageParameters);

        return new StorageAccount(storageAccount.Value);
    }

    public async Task<AppServicePlan> CreateAppServicePlanForFunctionApp(string name)
    {
        var appServicePlanData = new AppServicePlanData(Resource.Data.Location)
        {
            Sku = new AppServiceSkuDescription { Name = "Y1", Tier = "Dynamic" },
            Kind = "FunctionApp",
            IsReserved = true // Use linux
        };

        var appServicePlan = await Resource.GetAppServicePlans().CreateOrUpdateAsync(
            WaitUntil.Completed, name, appServicePlanData);

        return new AppServicePlan(appServicePlan.Value);
    }

    public async Task<AppServicePlan> CreateAppServicePlanForWebApp(string name)
    {
        var appServicePlanData = new AppServicePlanData(Resource.Data.Location)
        {
            Sku = new AppServiceSkuDescription { Name = "F1", Tier = "Free" },
            Kind = "linux",
            IsReserved = true // Use Linux
        };

        var appServicePlan = await Resource.GetAppServicePlans().CreateOrUpdateAsync(
            WaitUntil.Completed, name, appServicePlanData);

        return new AppServicePlan(appServicePlan.Value);
    }

    public async Task<ApplicationInsights> CreateApplicationInsights(string name)
    {
        var appInsightsData = new ApplicationInsightsComponentData(Resource.Data.Location, "web")
        {
            Kind = "web"
        };

        var appInsights = await Resource.GetApplicationInsightsComponents().CreateOrUpdateAsync(
            WaitUntil.Completed, name, appInsightsData);

        var armClient = AzureCloud.GetArmClient();
        var credential = AzureCloud.GetCredential();
        
        var workspaceResource = armClient.GetOperationalInsightsWorkspaceResource(appInsights.Value.Data.WorkspaceResourceId!);
        var workspace = await workspaceResource.GetAsync();
        
        var workspaceId = workspace.Value.Data.CustomerId.ToString();
        var logsQueryClient = new LogsQueryClient(credential);
        var connectionString = appInsights.Value.Data.ConnectionString ?? string.Empty;

        return new ApplicationInsights(workspaceId!, logsQueryClient, connectionString);
    }

    private static string SanitizeStorageAccountName(string name)
    {
        string storageAccountName = $"{name.ToLower().Replace("-", "")}storage";
        
        if (storageAccountName.Length > MaxStorageAccountNameLength)
        {
            storageAccountName = storageAccountName[..MaxStorageAccountNameLength];
        }
        
        return storageAccountName;
    }
}
