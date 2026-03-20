namespace AzureIntegration;

public class ManagedIdentity(string resourceId, Guid principalId, Guid clientId, string resourceGroupName, AzureCloud azureCloud) : IAsyncDisposable
{
    public string ResourceId => resourceId;
    public Guid PrincipalId => principalId;
    public Guid ClientId => clientId;

    public async ValueTask DisposeAsync()
    {
        await azureCloud.DeleteResourceGroup(resourceGroupName);
    }
}
