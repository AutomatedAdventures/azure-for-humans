using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AzureWebApp(WebSiteResource webApp, string resourceGroupName, AzureCloud azureCloud) : IAsyncDisposable
{
    public string Name => webApp.Data.Name;
    public string Url => webApp.Data.DefaultHostName;

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
