using AzureIntegration;

namespace AzureTests;

public class ManagedIdentityTests
{
    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task CreatingAUserAssignedIdentity_AssignsItAPrincipal(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string identityName = $"identity-{Guid.NewGuid().ToString("N")[..8]}";

        await using var identity = await azure.CreateUserAssignedIdentity(
            resourceGroupName: identityName,
            identityName: identityName);

        Assert.That(identity.PrincipalId, Is.Not.EqualTo(Guid.Empty),
            "A user-assigned identity should be assigned a principal by Azure so it can be granted access to resources.");
    }
}
