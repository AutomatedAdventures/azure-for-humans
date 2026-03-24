using Azure.Monitor.Query;
using AzureIntegration;

namespace AzureTests;

internal class InMemoryInfrastructure : IInfrastructure
{
    public string RegistryPassword { get; } = "fake-acr-password-s3cr3t";
    private const string AcrLoginServer = "fakeregistry.azurecr.io";
    private const string AcrUsername = "fakeuser";

    public List<string> CreatedResourceGroups { get; } = new();
    public List<string> DeletedResourceGroups { get; } = new();

    public Task CreateResourceGroup(string name)
    {
        CreatedResourceGroups.Add(name);
        return Task.CompletedTask;
    }

    public Task DeleteResourceGroup(string name)
    {
        DeletedResourceGroups.Add(name);
        return Task.CompletedTask;
    }

    public Task<ContainerRegistryInfo> CreateContainerRegistry(string resourceGroupName, string registryName)
    {
        return Task.FromResult(new ContainerRegistryInfo(AcrLoginServer, AcrUsername, RegistryPassword));
    }

    public Task<ApplicationInsights> CreateApplicationInsights(string resourceGroupName, string name)
    {
        return Task.FromResult(new ApplicationInsights("fake-workspace-id", new LogsQueryClient(new FakeCredential()), "InstrumentationKey=fake"));
    }

    public Task<string> CreateContainerAppsEnvironment(string resourceGroupName, string name)
    {
        return Task.FromResult($"/subscriptions/fake/resourceGroups/{resourceGroupName}/providers/Microsoft.App/managedEnvironments/{name}-env");
    }

    public Task<ContainerAppInfo> CreateContainerApp(
        string resourceGroupName,
        string environmentId,
        ContainerRegistryInfo registry,
        string name,
        string imageName,
        Dictionary<string, string>? environmentVariables,
        ApplicationInsights applicationInsights,
        string? managedIdentityResourceId)
    {
        return Task.FromResult(new ContainerAppInfo(name, $"{name}.azurecontainerapps.io", resourceGroupName, applicationInsights));
    }
}

internal class FakeCredential : Azure.Core.TokenCredential
{
    public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, System.Threading.CancellationToken cancellationToken)
        => new("fake-token", DateTimeOffset.MaxValue);

    public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, System.Threading.CancellationToken cancellationToken)
        => ValueTask.FromResult(new Azure.Core.AccessToken("fake-token", DateTimeOffset.MaxValue));
}
