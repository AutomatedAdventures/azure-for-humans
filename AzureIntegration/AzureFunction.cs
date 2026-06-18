using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AzureFunction(
    string name,
    string url,
    IApplicationLogs applicationInsights,
    string resourceGroupName,
    AzureCloud azureCloud)
    : IAsyncDisposable
{
    public string Name => name;
    public string Url => url;

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    public IEnumerable<string> GetLogsFromApplicationInsights() => applicationInsights.GetLogs();

    private async ValueTask DisposeAsync(bool deleteResourceGroup)
    {
        if (deleteResourceGroup)
        {
            await azureCloud.DeleteResourceGroup(resourceGroupName);
        }
    }
}
