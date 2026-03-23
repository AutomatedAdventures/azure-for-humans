using Azure;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.KeyVault.Models;

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
        var subscription = await azureCloud.GetSubscriptionAsync();
        var resourceGroup = await azureCloud.CreateResourceGroup(resourceGroupName);
        var tenantId = (await subscription.GetAsync()).Value.Data.TenantId!.Value;

        string vaultName = $"kv-{resourceGroupName.Replace("-", "")[..Math.Min(18, resourceGroupName.Replace("-", "").Length)]}";
        var vaultProperties = new KeyVaultProperties(tenantId, new KeyVaultSku(KeyVaultSkuFamily.A, KeyVaultSkuName.Standard));
        vaultProperties.AccessPolicies.Add(new KeyVaultAccessPolicy(tenantId, identityPrincipalId.ToString(),
            new IdentityAccessPermissions { Secrets = { IdentityAccessSecretPermission.Get } }));
        var vault = await resourceGroup.Resource.GetKeyVaults()
            .CreateOrUpdateAsync(WaitUntil.Completed, vaultName, new KeyVaultCreateOrUpdateContent(azureCloud.Location, vaultProperties));
        DeploymentLogger.Log($"Key Vault created with access policy for managed identity: {vault.Value.Data.Properties.VaultUri}");

        await vault.Value.GetKeyVaultSecrets().CreateOrUpdateAsync(
            WaitUntil.Completed, secretName,
            new KeyVaultSecretCreateOrUpdateContent(new SecretProperties { Value = secretValue }));
        DeploymentLogger.Log($"Secret '{secretName}' stored in Key Vault");

        return new AzureKeyVault(vault.Value.Data.Properties.VaultUri!.ToString(), resourceGroupName, azureCloud);
    }

    public async ValueTask DisposeAsync()
    {
        await azureCloud.DeleteResourceGroup(resourceGroupName);
    }
}
