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
        var identity = await azureCloud.ManagedIdentityService.Create(resourceGroup, identityName, azureCloud.Location);
        DeploymentLogger.Log($"Managed identity created: {identity.PrincipalId}");
        return new ManagedIdentity(
            identity.ResourceId,
            identity.PrincipalId,
            identity.ClientId,
            resourceGroupName,
            azureCloud);
    }

    public async ValueTask DisposeAsync()
    {
        await azureCloud.DeleteResourceGroup(resourceGroupName);
    }
}
