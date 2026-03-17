namespace AzureIntegration;

public class AzureContainerApp(string name, string fqdn, string resourceGroupName, AzureCloud azureCloud, ApplicationInsights applicationInsights) : IAsyncDisposable
{
    public string Name => name;
    public string Fqdn => fqdn;
    public string Url => $"https://{fqdn}";

    public IEnumerable<string> GetLogsFromApplicationInsights() => applicationInsights.GetLogs();

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsync(bool deleteResourceGroup)
    {
        if (!deleteResourceGroup)
        {
            return;
        }

        DeploymentLogger.Log($"Disposing Container App '{name}', deleting resource group...");
        try
        {
            await azureCloud.DeleteResourceGroup(resourceGroupName);
            DeploymentLogger.Log($"Resource group '{resourceGroupName}' deleted");
        }
        catch (Exception ex)
        {
            DeploymentLogger.LogError($"Failed to delete resource group '{resourceGroupName}': {ex.Message}");
        }
    }
}
