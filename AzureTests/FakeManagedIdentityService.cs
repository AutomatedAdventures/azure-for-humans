using Azure.Core;
using AzureIntegration;

namespace AzureTests;

public class FakeManagedIdentityService : IManagedIdentityService
{
    public Task<ManagedIdentityDeployment> Create(ResourceGroup resourceGroup, string identityName, AzureLocation location) =>
        Task.FromResult(new ManagedIdentityDeployment(
            ResourceId: $"/identities/{identityName}",
            PrincipalId: Guid.NewGuid(),
            ClientId: Guid.NewGuid()));
}
