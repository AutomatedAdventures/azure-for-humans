using Azure.Core;
using AzureIntegration;

namespace AzureTests;

public class FakeKeyVaultService : IKeyVaultService
{
    private readonly Dictionary<(string VaultUri, string SecretName), string> _secrets = new();

    public Task<KeyVaultDeployment> Create(
        ResourceGroup resourceGroup,
        string resourceGroupName,
        string secretName,
        string secretValue,
        Guid identityPrincipalId,
        AzureLocation location)
    {
        string vaultUri = $"https://{resourceGroupName}.vault.azure.net/";
        _secrets[(vaultUri, secretName)] = secretValue;
        return Task.FromResult(new KeyVaultDeployment(vaultUri));
    }

    public Task<string> ReadSecret(string vaultUri, string secretName) =>
        Task.FromResult(_secrets[(vaultUri, secretName)]);
}
