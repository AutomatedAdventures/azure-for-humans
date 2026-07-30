using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AzureWebApp(string name, string url, string resourceGroupName, AzureCloud azureCloud) : IAsyncDisposable
{
    public string Name => name;
    public string Url => url;

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsync(bool deleteResourceGroup)
    {
        if (deleteResourceGroup)
        {
            await azureCloud.DeleteResourceGroup(resourceGroupName);
        }
    }
}
