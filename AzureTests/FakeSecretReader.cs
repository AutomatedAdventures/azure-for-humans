using ContainerApp;

namespace AzureTests;

public class FakeSecretReader(FakeKeyVaultService keyVault) : ISecretReader
{
    public Task<string> Read(string vaultUri, string secretName) =>
        keyVault.ReadSecret(vaultUri, secretName);
}
