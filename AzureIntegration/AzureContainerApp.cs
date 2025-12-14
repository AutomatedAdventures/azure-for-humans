namespace AzureIntegration;

public class AzureContainerApp(string name, string resourceGroupName, AzureCloud azureCloud) : IAsyncDisposable
{
    public string Name => name;

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
