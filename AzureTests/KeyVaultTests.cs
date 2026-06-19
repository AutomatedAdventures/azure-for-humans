using AzureIntegration;

namespace AzureTests;

public class KeyVaultTests
{
    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task ASecretStoredInAKeyVault_CanBeReadBack(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string name = $"vault-{Guid.NewGuid().ToString("N")[..8]}";
        string secretValue = Guid.NewGuid().ToString();

        await using var identity = await azure.CreateUserAssignedIdentity(
            resourceGroupName: $"{name}-id",
            identityName: $"{name}-id");

        await using var keyVault = await azure.CreateKeyVaultWithSecret(
            resourceGroupName: name,
            secretName: "test-secret",
            secretValue: secretValue,
            identityPrincipalId: identity.PrincipalId);

        string storedSecret = await keyVault.ReadSecret("test-secret");

        Assert.That(storedSecret, Is.EqualTo(secretValue),
            "A secret stored in the key vault should be readable from it.");
    }
}
