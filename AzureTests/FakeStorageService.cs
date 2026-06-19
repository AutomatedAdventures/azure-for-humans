using AzureIntegration;

namespace AzureTests;

public class FakeStorageService : IStorageService
{
    public Task<StorageAccountDeployment> Create(ResourceGroup resourceGroup, string name)
    {
        string accountName = $"{name}storage";
        string connectionString =
            $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey=fake-key;EndpointSuffix=core.windows.net";
        return Task.FromResult(new StorageAccountDeployment(accountName, connectionString));
    }
}
