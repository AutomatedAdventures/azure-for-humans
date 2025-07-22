using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AzureFunction(WebSiteResource functionApp, string resourceGroupName, AzureCloud azureCloud)
    : IAsyncDisposable
{
    public string Name => functionApp.Data.Name;
    public string Url => functionApp.Data.DefaultHostName;

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
