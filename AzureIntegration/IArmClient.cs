using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

namespace AzureIntegration;

public interface IArmClient
{
    Task<ISubscriptionResource> GetDefaultSubscriptionAsync();
}

public interface ISubscriptionResource
{
    IResourceGroupCollection GetResourceGroups();
}

public interface IResourceGroupCollection
{
    Task<IResourceGroupResource> CreateOrUpdateAsync(WaitUntil waitUntil, string name, ResourceGroupData data);
}

public interface IResourceGroupResource
{
    ResourceGroupResource? Concrete { get; }
}

internal class ArmClientAdapter(ArmClient armClient) : IArmClient
{
    public async Task<ISubscriptionResource> GetDefaultSubscriptionAsync() =>
        new SubscriptionResourceAdapter(await armClient.GetDefaultSubscriptionAsync());
}

internal class SubscriptionResourceAdapter(SubscriptionResource subscription) : ISubscriptionResource
{
    public IResourceGroupCollection GetResourceGroups() =>
        new ResourceGroupCollectionAdapter(subscription.GetResourceGroups());
}

internal class ResourceGroupCollectionAdapter(ResourceGroupCollection collection) : IResourceGroupCollection
{
    public async Task<IResourceGroupResource> CreateOrUpdateAsync(WaitUntil waitUntil, string name, ResourceGroupData data)
    {
        var operation = await collection.CreateOrUpdateAsync(waitUntil, name, data);
        return new ResourceGroupResourceAdapter(operation.Value);
    }
}

internal class ResourceGroupResourceAdapter(ResourceGroupResource resource) : IResourceGroupResource
{
    public ResourceGroupResource? Concrete => resource;
}
