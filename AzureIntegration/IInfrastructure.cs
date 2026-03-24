namespace AzureIntegration;

internal record ContainerRegistryInfo(string LoginServer, string Username, string Password);

internal record ContainerAppInfo(string Name, string Fqdn, string ResourceGroupName, ApplicationInsights ApplicationInsights);

internal interface IInfrastructure
{
    Task CreateResourceGroup(string name);
    Task DeleteResourceGroup(string name);
    Task<ContainerRegistryInfo> CreateContainerRegistry(string resourceGroupName, string registryName);
    Task<ApplicationInsights> CreateApplicationInsights(string resourceGroupName, string name);
    Task<string> CreateContainerAppsEnvironment(string resourceGroupName, string name);
    Task<ContainerAppInfo> CreateContainerApp(
        string resourceGroupName,
        string environmentId,
        ContainerRegistryInfo registry,
        string name,
        string imageName,
        Dictionary<string, string>? environmentVariables,
        ApplicationInsights applicationInsights,
        string? managedIdentityResourceId);
}
