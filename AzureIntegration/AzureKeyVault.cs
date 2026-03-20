namespace AzureIntegration;

public class AzureKeyVault(string uri, string resourceGroupName, AzureCloud azureCloud) : IAsyncDisposable
{
    public string Uri => uri;

    public async ValueTask DisposeAsync()
    {
        await azureCloud.DeleteResourceGroup(resourceGroupName);
    }
}
