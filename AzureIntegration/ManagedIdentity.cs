using Azure;
using Azure.ResourceManager.ManagedServiceIdentities;
using Azure.ResourceManager.ManagedServiceIdentities.Models;

namespace AzureIntegration;

public class ManagedIdentity(string resourceId, Guid principalId, Guid clientId, string resourceGroupName, AzureCloud azureCloud) : IAsyncDisposable
{
    public string ResourceId => resourceId;
    public Guid PrincipalId => principalId;
    public Guid ClientId => clientId;

    public static async Task<ManagedIdentity> CreateAsync(AzureCloud azureCloud, string resourceGroupName, string identityName)
    {
        DeploymentLogger.Log($"Creating user-assigned managed identity '{identityName}' in '{resourceGroupName}'...");
        var resourceGroup = await azureCloud.CreateResourceGroup(resourceGroupName);
        var identity = await resourceGroup.Resource.GetUserAssignedIdentities()
            .CreateOrUpdateAsync(WaitUntil.Completed, identityName, new UserAssignedIdentityData(azureCloud.Location));
        DeploymentLogger.Log($"Managed identity created: {identity.Value.Data.PrincipalId}");
        return new ManagedIdentity(
            identity.Value.Id.ToString(),
            identity.Value.Data.PrincipalId!.Value,
            identity.Value.Data.ClientId!.Value,
            resourceGroupName,
            azureCloud);
    }

    public async ValueTask DisposeAsync()
    {
        await azureCloud.DeleteResourceGroup(resourceGroupName);
    }
}
