namespace AzureIntegration;

public class AzureContainerApp(string name, string fqdn, string resourceGroupName, AzureCloud azureCloud) : IAsyncDisposable
{
    public string Name => name;
    public string Fqdn => fqdn;
    public string Url => $"https://{fqdn}";

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsync(bool deleteResourceGroup)
    {
        if (deleteResourceGroup)
        {
            Console.WriteLine($"Disposing AzureContainerApp '{name}', deleting resource group '{resourceGroupName}'...");
            Console.Out.Flush();
            try
            {
                await azureCloud.DeleteResourceGroup(resourceGroupName);
                Console.WriteLine($"Resource group '{resourceGroupName}' deleted successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to delete resource group '{resourceGroupName}': {ex.Message}");
            }
            Console.Out.Flush();
        }
    }
}
