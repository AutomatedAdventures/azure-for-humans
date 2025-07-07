using Azure.ResourceManager.Storage;

namespace AzureIntegration;

public class StorageAccount(StorageAccountResource storageAccount)
{
    private string? _connectionString;

    public string Name => storageAccount.Data.Name;
    public string ConnectionString => _connectionString ??= GetConnectionStringAsync();

    private string GetConnectionStringAsync()
    {
        var keys = storageAccount.GetKeysAsync().ToBlockingEnumerable();
        string? key = keys.First().Value;
        return $"DefaultEndpointsProtocol=https;AccountName={Name};AccountKey={key};EndpointSuffix=core.windows.net";
    }
}
