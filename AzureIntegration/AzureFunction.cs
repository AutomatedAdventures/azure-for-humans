using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AzureFunction : IAsyncDisposable
{
    private readonly WebSiteResource _functionApp;
    private readonly string _resourceGroupName;
    private readonly AzureCloud _azureCloud;

    public AzureFunction(WebSiteResource functionApp, string resourceGroupName, AzureCloud azureCloud)
    {
        _functionApp = functionApp;
        _resourceGroupName = resourceGroupName;
        _azureCloud = azureCloud;
    }

    public string Name => _functionApp.Data.Name;
    public string Url => _functionApp.Data.DefaultHostName;

    public async ValueTask DisposeAsync()
    {
        await _azureCloud.DeleteResourceGroup(_resourceGroupName);
    }
}
