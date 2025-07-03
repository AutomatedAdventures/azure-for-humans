using Azure.ResourceManager.Storage;

namespace AzureIntegration;

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
