using Azure;
using Azure.Core;
using Azure.ResourceManager.ContainerRegistry;
using Azure.ResourceManager.Resources;
using AzureIntegration;

namespace AzureTests;

public class FakeArmClient : IArmClient
{
    private readonly HashSet<AzureLocation> _regionsWithoutCapacity = new();
    private readonly Dictionary<string, FakeRegistry> _registries = new();

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

    internal FakeRegistry CreateRegistry(string name)
    {
        var registry = new FakeRegistry { LoginServer = $"{name.ToLower()}.azurecr.io" };
        _registries[registry.LoginServer] = registry;
        return registry;
    }

    internal FakeRegistry? RegistryAt(string loginServer) =>
        _registries.TryGetValue(loginServer, out var registry) ? registry : null;

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
        return Task.FromResult<IResourceGroupResource>(new FakeResourceGroupResource(armClient));
    }

    public Task<IResourceGroupResource> GetAsync(string name) =>
        Task.FromResult<IResourceGroupResource>(new FakeResourceGroupResource(armClient));
}

internal class FakeResourceGroupResource(FakeArmClient armClient) : IResourceGroupResource
{
    public ResourceGroupResource? Concrete => null;

    public IContainerRegistryCollection GetContainerRegistries() =>
        new FakeContainerRegistryCollection(armClient);

    public Task DeleteAsync(WaitUntil waitUntil) => Task.CompletedTask;
}

internal class FakeContainerRegistryCollection(FakeArmClient armClient) : IContainerRegistryCollection
{
    public Task<IContainerRegistryResource> CreateOrUpdateAsync(WaitUntil waitUntil, string registryName, ContainerRegistryData data)
    {
        var registry = armClient.CreateRegistry(registryName);
        return Task.FromResult<IContainerRegistryResource>(new FakeContainerRegistryResource(registry));
    }
}

internal class FakeContainerRegistryResource(FakeRegistry registry) : IContainerRegistryResource
{
    public string Id => $"/fake/registries/{registry.LoginServer}";
    public string LoginServer => registry.LoginServer;

    public Task<IContainerRegistryCredentials> GetCredentialsAsync() =>
        Task.FromResult<IContainerRegistryCredentials>(new FakeContainerRegistryCredentials());
}

internal class FakeContainerRegistryCredentials : IContainerRegistryCredentials
{
    public string Username => "fake-user";
    public string Password => "fake-password";
}

internal class FakeRegistry
{
    public required string LoginServer { get; init; }
    public HashSet<string> Images { get; } = new();
}
