using Azure;
using Azure.Core;
using Azure.ResourceManager.Resources;
using AzureIntegration;

namespace AzureTests;

public class FakeArmClient : IArmClient
{
    private readonly HashSet<AzureLocation> _regionsWithoutContainerAppCapacity = new();
    private readonly Dictionary<string, FakeRegistry> _registries = new();
    private readonly Dictionary<string, AzureLocation> _resourceGroups = new();
    private readonly Dictionary<string, FakeWebApp> _webApps = new();
    private readonly Dictionary<string, string> _imageProjects = new();
    private readonly Dictionary<string, Dictionary<string, string>> _imageBuildArguments = new();

    internal void RecordImageProject(string imageTag, string project) => _imageProjects[imageTag] = project;

    internal string? ProjectForImage(string imageTag) => _imageProjects.GetValueOrDefault(imageTag);

    internal void RecordImageBuildArguments(string imageTag, Dictionary<string, string> buildArguments) =>
        _imageBuildArguments[imageTag] = buildArguments;

    internal Dictionary<string, string> BuildArgumentsForImage(string imageTag) =>
        _imageBuildArguments.GetValueOrDefault(imageTag) ?? new();

    internal FakeWebApp RecordWebApp(string host, string projectDirectory, Dictionary<string, string>? environmentVariables)
    {
        var app = new FakeWebApp(projectDirectory, environmentVariables ?? new());
        _webApps[host] = app;
        return app;
    }

    internal FakeWebApp? WebAppAt(string host) => _webApps.GetValueOrDefault(host);

    public FakeArmClient ThatHasNoContainerAppCapacityIn(AzureLocation location)
    {
        _regionsWithoutContainerAppCapacity.Add(location);
        return this;
    }

    internal bool HasContainerAppCapacityIn(AzureLocation location) =>
        !_regionsWithoutContainerAppCapacity.Contains(location);

    public Task<ISubscriptionResource> GetDefaultSubscriptionAsync() =>
        Task.FromResult<ISubscriptionResource>(new FakeSubscriptionResource(this));

    public IContainerRegistryResource GetContainerRegistryResource(ResourceIdentifier id)
    {
        string loginServer = id.ToString().Split('/').Last();
        return new FakeContainerRegistryResource(this, loginServer);
    }

    internal FakeRegistry CreateRegistry(string name)
    {
        var registry = new FakeRegistry { LoginServer = $"{name.ToLower()}.azurecr.io" };
        _registries[registry.LoginServer] = registry;
        return registry;
    }

    internal FakeRegistry? RegistryAt(string loginServer) =>
        _registries.TryGetValue(loginServer, out var registry) ? registry : null;

    internal void DeleteRegistry(string name) =>
        _registries.Remove($"{name.ToLower()}.azurecr.io");

    internal void RecordResourceGroup(string name, AzureLocation location) => _resourceGroups[name] = location;

    internal void RemoveResourceGroup(string name) => _resourceGroups.Remove(name);

    internal bool HasResourceGroup(string name) => _resourceGroups.ContainsKey(name);

    internal IReadOnlyList<(string Name, AzureLocation Location)> ResourceGroups =>
        _resourceGroups.Select(entry => (entry.Key, entry.Value)).ToList();

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
        armClient.RecordResourceGroup(name, region);
        return Task.FromResult<IResourceGroupResource>(new FakeResourceGroupResource(armClient, name, region));
    }

    public Task<IResourceGroupResource> GetAsync(string name) =>
        Task.FromResult<IResourceGroupResource>(new FakeResourceGroupResource(armClient, name, default));

    public Task<bool> ExistsAsync(string name) =>
        Task.FromResult(armClient.HasResourceGroup(name));

    public Task<IReadOnlyList<IResourceGroupResource>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<IResourceGroupResource>>(
            armClient.ResourceGroups
                .Select(group => (IResourceGroupResource)new FakeResourceGroupResource(armClient, group.Name, group.Location))
                .ToList());
}

internal class FakeResourceGroupResource(FakeArmClient armClient, string name, AzureLocation location) : IResourceGroupResource
{
    public ResourceGroupResource? Concrete => null;

    public string Name => name;

    public AzureLocation Location => location;

    public IContainerRegistryCollection GetContainerRegistries() =>
        new FakeContainerRegistryCollection(armClient);

    public Task DeleteAsync(WaitUntil waitUntil)
    {
        armClient.DeleteRegistry(name);
        armClient.RemoveResourceGroup(name);
        return Task.CompletedTask;
    }
}

internal class FakeContainerRegistryCollection(FakeArmClient armClient) : IContainerRegistryCollection
{
    public Task<IContainerRegistryResource> CreateOrUpdateAsync(WaitUntil waitUntil, string registryName, AzureLocation location)
    {
        var registry = armClient.CreateRegistry(registryName);
        return Task.FromResult<IContainerRegistryResource>(new FakeContainerRegistryResource(armClient, registry.LoginServer));
    }
}

internal class FakeContainerRegistryResource(FakeArmClient armClient, string loginServer) : IContainerRegistryResource
{
    public string Id => $"/fake/registries/{loginServer}";
    public string LoginServer => loginServer;

    public Task<IContainerRegistryCredentials> GetCredentialsAsync() =>
        Task.FromResult<IContainerRegistryCredentials>(new FakeContainerRegistryCredentials());

    public Task<bool> Exists() =>
        Task.FromResult(armClient.RegistryAt(loginServer) != null);
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
