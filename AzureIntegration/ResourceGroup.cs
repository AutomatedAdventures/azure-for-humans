using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure;

namespace AzureIntegration;

public record ResourceGroup(ResourceGroupResource Resource)
{
    public string Name => Resource.Data.Name;

    public async Task<StorageAccount> CreateStorageAccount(string name)
    {
        string storageAccountName = $"{name.ToLower().Replace("-", "")}storage";
        if (storageAccountName.Length > 24)
        {
            storageAccountName = storageAccountName.Substring(0, 24);
        }
        var storageSku = new StorageSku(StorageSkuName.StandardLrs);
        var storageKind = StorageKind.StorageV2;
        var storageParameters = new StorageAccountCreateOrUpdateContent(storageSku, storageKind, location: Resource.Data.Location);

        var storageAccount = await Resource.GetStorageAccounts().CreateOrUpdateAsync(
            WaitUntil.Completed, storageAccountName, storageParameters);

        return new StorageAccount(storageAccount.Value);
    }

    public async Task<AppServicePlan> CreateAppServicePlan(string name)
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
}
