using Azure;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;

namespace AzureIntegration;

public interface IStorageService
{
    Task<StorageAccountDeployment> Create(ResourceGroup resourceGroup, string name);
}

public record StorageAccountDeployment(string Name, string ConnectionString);

public class RealStorageService : IStorageService
{
    private const int MaxStorageAccountNameLength = 24;

    public async Task<StorageAccountDeployment> Create(ResourceGroup resourceGroup, string name)
    {
        string storageAccountName = SanitizeStorageAccountName(name);
        var storageParameters = new StorageAccountCreateOrUpdateContent(
            new StorageSku(StorageSkuName.StandardLrs),
            StorageKind.StorageV2,
            location: resourceGroup.Resource.Data.Location);

        var storageAccount = await resourceGroup.Resource.GetStorageAccounts().CreateOrUpdateAsync(
            WaitUntil.Completed, storageAccountName, storageParameters);

        return new StorageAccountDeployment(
            storageAccount.Value.Data.Name,
            ConnectionStringFor(storageAccount.Value));
    }

    private static string ConnectionStringFor(StorageAccountResource storageAccount)
    {
        var keys = storageAccount.GetKeysAsync().ToBlockingEnumerable();
        string? key = keys.First().Value;
        return $"DefaultEndpointsProtocol=https;AccountName={storageAccount.Data.Name};AccountKey={key};EndpointSuffix=core.windows.net";
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

public class FailingStorageService : IStorageService
{
    public Task<StorageAccountDeployment> Create(ResourceGroup resourceGroup, string name) =>
        throw new NotSupportedException("This AzureCloud was not configured to create storage accounts.");
}
