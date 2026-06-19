using Azure;
using Azure.Core;
using Azure.ResourceManager.ManagedServiceIdentities;
using Azure.ResourceManager.ManagedServiceIdentities.Models;

namespace AzureIntegration;

public interface IManagedIdentityService
{
    Task<ManagedIdentityDeployment> Create(ResourceGroup resourceGroup, string identityName, AzureLocation location);
}

public record ManagedIdentityDeployment(string ResourceId, Guid PrincipalId, Guid ClientId);

public class RealManagedIdentityService : IManagedIdentityService
{
    public async Task<ManagedIdentityDeployment> Create(ResourceGroup resourceGroup, string identityName, AzureLocation location)
    {
        var identity = await resourceGroup.Resource.GetUserAssignedIdentities()
            .CreateOrUpdateAsync(WaitUntil.Completed, identityName, new UserAssignedIdentityData(location));
        return new ManagedIdentityDeployment(
            identity.Value.Id.ToString(),
            identity.Value.Data.PrincipalId!.Value,
            identity.Value.Data.ClientId!.Value);
    }
}

public class FailingManagedIdentityService : IManagedIdentityService
{
    public Task<ManagedIdentityDeployment> Create(ResourceGroup resourceGroup, string identityName, AzureLocation location) =>
        throw new NotSupportedException("This AzureCloud was not configured to create managed identities.");
}
