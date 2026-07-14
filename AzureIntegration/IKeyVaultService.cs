using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.KeyVault.Models;
using Azure.Security.KeyVault.Secrets;

namespace AzureIntegration;

public interface IKeyVaultService
{
    Task<KeyVaultDeployment> Create(
        ResourceGroup resourceGroup,
        string resourceGroupName,
        string secretName,
        string secretValue,
        Guid identityPrincipalId,
        AzureLocation location);

    Task<string> ReadSecret(string vaultUri, string secretName);
}

public record KeyVaultDeployment(string Uri);

public class RealKeyVaultService(TokenCredential credentials) : IKeyVaultService
{
    public async Task<KeyVaultDeployment> Create(
        ResourceGroup resourceGroup,
        string resourceGroupName,
        string secretName,
        string secretValue,
        Guid identityPrincipalId,
        AzureLocation location)
    {
        var subscription = await new ArmClient(credentials).GetDefaultSubscriptionAsync();
        var tenantId = (await subscription.GetAsync()).Value.Data.TenantId!.Value;

        string vaultName = $"kv-{resourceGroupName.Replace("-", "")[..Math.Min(18, resourceGroupName.Replace("-", "").Length)]}";
        var vaultProperties = new KeyVaultProperties(tenantId, new KeyVaultSku(KeyVaultSkuFamily.A, KeyVaultSkuName.Standard));
        vaultProperties.AccessPolicies.Add(new KeyVaultAccessPolicy(tenantId, identityPrincipalId.ToString(),
            new IdentityAccessPermissions { Secrets = { IdentityAccessSecretPermission.Get } }));
        vaultProperties.AccessPolicies.Add(new KeyVaultAccessPolicy(tenantId, await GetDeployerPrincipalId(),
            new IdentityAccessPermissions { Secrets = { IdentityAccessSecretPermission.Get, IdentityAccessSecretPermission.Set } }));
        var vault = await resourceGroup.Resource.GetKeyVaults()
            .CreateOrUpdateAsync(WaitUntil.Completed, vaultName, new KeyVaultCreateOrUpdateContent(location, vaultProperties));
        DeploymentLogger.Log($"Key Vault created with access policy for managed identity: {vault.Value.Data.Properties.VaultUri}");

        await vault.Value.GetKeyVaultSecrets().CreateOrUpdateAsync(
            WaitUntil.Completed, secretName,
            new KeyVaultSecretCreateOrUpdateContent(new Azure.ResourceManager.KeyVault.Models.SecretProperties { Value = secretValue }));
        DeploymentLogger.Log($"Secret '{secretName}' stored in Key Vault");

        return new KeyVaultDeployment(vault.Value.Data.Properties.VaultUri!.ToString());
    }

    public async Task<string> ReadSecret(string vaultUri, string secretName)
    {
        var client = new SecretClient(new Uri(vaultUri), credentials);
        return (await client.GetSecretAsync(secretName)).Value.Value;
    }

    private async Task<string> GetDeployerPrincipalId()
    {
        var token = await credentials.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]), CancellationToken.None);
        return DecodeJwtClaims(token.Token).GetProperty("oid").GetString()!;
    }

    private static JsonElement DecodeJwtClaims(string jwt)
    {
        string payload = jwt.Split('.')[1];
        payload = payload.Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;
    }
}

public class FailingKeyVaultService : IKeyVaultService
{
    public Task<KeyVaultDeployment> Create(
        ResourceGroup resourceGroup,
        string resourceGroupName,
        string secretName,
        string secretValue,
        Guid identityPrincipalId,
        AzureLocation location) =>
        throw new NotSupportedException("This AzureCloud was not configured to create key vaults.");

    public Task<string> ReadSecret(string vaultUri, string secretName) =>
        throw new NotSupportedException("This AzureCloud was not configured to create key vaults.");
}
