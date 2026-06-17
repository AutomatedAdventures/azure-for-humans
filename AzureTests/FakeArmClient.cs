using Azure;
using Azure.Core;
using Azure.ResourceManager.Resources;
using AzureIntegration;

namespace AzureTests;

public class FakeArmClient : IArmClient
{
    private readonly HashSet<AzureLocation> _regionsWithoutCapacity = new();

    public AzureLocation? RegionWhereResourceGroupWasCreated { get; private set; }

    public FakeArmClient ThatHasNoCapacityIn(AzureLocation location)
    {
        _regionsWithoutCapacity.Add(location);
        return this;
    }

    public Task<ISubscriptionResource> GetDefaultSubscriptionAsync() =>
        Task.FromResult<ISubscriptionResource>(new FakeSubscriptionResource(this));

    internal bool HasCapacityIn(AzureLocation location) =>
        !_regionsWithoutCapacity.Contains(location);

    internal void RecordResourceGroupCreatedIn(AzureLocation location) =>
        RegionWhereResourceGroupWasCreated = location;

    internal static RequestFailedException CapacityError(AzureLocation location) =>
        new(status: 400,
            message: $"AKS is experiencing heavy usage in region {location}.",
            errorCode: "AKSCapacityHeavyUsage",
            innerException: null);
}

internal class FakeSubscriptionResource(FakeArmClient armClient) : ISubscriptionResource
{
    public IResourceGroupCollection GetResourceGroups() =>
        new FakeResourceGroupCollection(armClient);
}

internal class FakeResourceGroupCollection(FakeArmClient armClient) : IResourceGroupCollection
{
    public Task<IResourceGroupResource> CreateOrUpdateAsync(WaitUntil waitUntil, string name, ResourceGroupData data)
    {
        var region = data.Location;
        if (!armClient.HasCapacityIn(region))
        {
            throw FakeArmClient.CapacityError(region);
        }

        armClient.RecordResourceGroupCreatedIn(region);
        return Task.FromResult<IResourceGroupResource>(new FakeResourceGroupResource());
    }
}

internal class FakeResourceGroupResource : IResourceGroupResource
{
    public ResourceGroupResource? Concrete => null;
}
