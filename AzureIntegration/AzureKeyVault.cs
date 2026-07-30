namespace AzureIntegration;

public class AzureKeyVault(string uri, string resourceGroupName, AzureCloud azureCloud) : IAsyncDisposable
{
    public string Uri => uri;

    public static async Task<AzureKeyVault> CreateAsync(
        AzureCloud azureCloud,
        string resourceGroupName,
        string secretName,
        string secretValue,
        Guid identityPrincipalId)
    {
        DeploymentLogger.Log($"Creating Key Vault in resource group '{resourceGroupName}'...");
        var resourceGroup = await azureCloud.CreateResourceGroup(resourceGroupName);
        var deployment = await azureCloud.KeyVaultService.Create(
            resourceGroup, resourceGroupName, secretName, secretValue, identityPrincipalId, azureCloud.Location);
        return new AzureKeyVault(deployment.Uri, resourceGroupName, azureCloud);
    }

    public Task<string> ReadSecret(string secretName) =>
        azureCloud.KeyVaultService.ReadSecret(uri, secretName);

    public async ValueTask DisposeAsync()
    {
        await azureCloud.DeleteResourceGroup(resourceGroupName);
    }
}
