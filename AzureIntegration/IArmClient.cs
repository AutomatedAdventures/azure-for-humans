using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerRegistry;
using Azure.ResourceManager.ContainerRegistry.Models;
using Azure.ResourceManager.Resources;

namespace AzureIntegration;

public interface IArmClient
{
    Task<ISubscriptionResource> GetDefaultSubscriptionAsync();
    IContainerRegistryResource GetContainerRegistryResource(ResourceIdentifier id);
}

public interface ISubscriptionResource
{
    IResourceGroupCollection GetResourceGroups();
}

public interface IResourceGroupCollection
{
    Task<IResourceGroupResource> CreateOrUpdateAsync(WaitUntil waitUntil, string name, ResourceGroupData data);
    Task<IResourceGroupResource> GetAsync(string name);
}

public interface IResourceGroupResource
{
    ResourceGroupResource? Concrete { get; }
    IContainerRegistryCollection GetContainerRegistries();
    Task DeleteAsync(WaitUntil waitUntil);
}

public interface IContainerRegistryCollection
{
    Task<IContainerRegistryResource> CreateOrUpdateAsync(WaitUntil waitUntil, string registryName, ContainerRegistryData data);
}

public interface IContainerRegistryResource
{
    string Id { get; }
    string LoginServer { get; }
    Task<IContainerRegistryCredentials> GetCredentialsAsync();
    Task<bool> Exists();
}

public interface IContainerRegistryCredentials
{
    string Username { get; }
    string Password { get; }
}

internal class ArmClientAdapter(ArmClient armClient) : IArmClient
{
    public async Task<ISubscriptionResource> GetDefaultSubscriptionAsync() =>
        new SubscriptionResourceAdapter(await armClient.GetDefaultSubscriptionAsync());

    public IContainerRegistryResource GetContainerRegistryResource(ResourceIdentifier id) =>
        new ContainerRegistryResourceAdapter(armClient.GetContainerRegistryResource(id));
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

    public async Task<IResourceGroupResource> GetAsync(string name)
    {
        var resource = await collection.GetAsync(name);
        return new ResourceGroupResourceAdapter(resource.Value);
    }
}

internal class ResourceGroupResourceAdapter(ResourceGroupResource resource) : IResourceGroupResource
{
    public ResourceGroupResource? Concrete => resource;

    public IContainerRegistryCollection GetContainerRegistries() =>
        new ContainerRegistryCollectionAdapter(resource.GetContainerRegistries());

    public async Task DeleteAsync(WaitUntil waitUntil) =>
        await resource.DeleteAsync(waitUntil);
}

internal class ContainerRegistryCollectionAdapter(ContainerRegistryCollection collection) : IContainerRegistryCollection
{
    public async Task<IContainerRegistryResource> CreateOrUpdateAsync(WaitUntil waitUntil, string registryName, ContainerRegistryData data)
    {
        var operation = await collection.CreateOrUpdateAsync(waitUntil, registryName, data);
        return new ContainerRegistryResourceAdapter(operation.Value);
    }
}

internal class ContainerRegistryResourceAdapter(ContainerRegistryResource resource) : IContainerRegistryResource
{
    public string Id => resource.Id.ToString();
    public string LoginServer => resource.Data.LoginServer;

    public async Task<IContainerRegistryCredentials> GetCredentialsAsync()
    {
        var credentials = await resource.GetCredentialsAsync();
        return new ContainerRegistryCredentialsAdapter(credentials.Value);
    }

    public async Task<bool> Exists()
    {
        try
        {
            await resource.GetAsync();
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }
}

internal class ContainerRegistryCredentialsAdapter(ContainerRegistryListCredentialsResult credentials) : IContainerRegistryCredentials
{
    public string Username => credentials.Username;
    public string Password => credentials.Passwords.First().Value;
}
