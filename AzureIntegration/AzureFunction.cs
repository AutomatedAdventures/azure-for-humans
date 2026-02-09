using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AzureFunction(
    WebSiteResource functionApp, 
    ApplicationInsights applicationInsights, 
    string resourceGroupName, 
    AzureCloud azureCloud)
    : IAsyncDisposable
{
    public string Name => functionApp.Data.Name;
    public string Url => functionApp.Data.DefaultHostName;

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    public IEnumerable<string> GetLogsFromApplicationInsights()
    {
        return applicationInsights.GetLogs();
    }

    private async ValueTask DisposeAsync(bool deleteResourceGroup)
    {
        if (deleteResourceGroup)
        {
            await azureCloud.DeleteResourceGroup(resourceGroupName);
        }
    }
}
