using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AzureWebApp : IAsyncDisposable
{
    private readonly WebSiteResource _webApp;
    private readonly string _resourceGroupName;
    private readonly AzureCloud _azureCloud;

    public AzureWebApp(WebSiteResource webApp, string resourceGroupName, AzureCloud azureCloud)
    {
        _webApp = webApp;
        _resourceGroupName = resourceGroupName;
        _azureCloud = azureCloud;
    }

    public string Name => _webApp.Data.Name;
    public string Url => _webApp.Data.DefaultHostName;

    public async ValueTask DisposeAsync()
    {
        await _azureCloud.DeleteResourceGroup(_resourceGroupName);
    }
}
