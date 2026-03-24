using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.ApplicationInsights;
using Azure.ResourceManager.ContainerRegistry;
using Azure.ResourceManager.ContainerRegistry.Models;
using Azure.ResourceManager.OperationalInsights;
using Azure.ResourceManager.Resources;

namespace AzureIntegration;

internal class ArmInfrastructure : IInfrastructure
{
    private readonly ArmClient _armClient;
    private readonly TokenCredential _credential;
    private readonly AzureLocation _location;
    private SubscriptionResource? _subscription;

    internal static string SanitizeAcrName(string name)
    {
        string acrName = $"{name.ToLower().Replace("-", "")}acr";
        return acrName.Length > 50 ? acrName[..50] : acrName;
    }

    internal ArmInfrastructure(TokenCredential credential, AzureLocation location)
    {
        _credential = credential;
        _armClient = new ArmClient(credential);
        _location = location;
    }

    internal async Task<SubscriptionResource> GetSubscriptionAsync()
    {
        if (_subscription == null)
        {
            try
            {
                _subscription = await _armClient.GetDefaultSubscriptionAsync();
            }
            catch (AuthenticationFailedException)
            {
                throw new AuthenticationFailedException("Invalid credentials provided. Please check your client ID, client secret, and tenant ID.");
            }
            catch (Exception ex) when (ex.InnerException is AuthenticationFailedException)
            {
                throw new AuthenticationFailedException("Invalid credentials provided. Please check your client ID, client secret, and tenant ID.");
            }
        }
        return _subscription;
    }

    internal ArmClient GetArmClient() => _armClient;
    internal TokenCredential GetCredential() => _credential;

    internal async Task<ResourceGroupResource> GetResourceGroupResource(string name)
    {
        var subscription = await GetSubscriptionAsync();
        return (await subscription.GetResourceGroups().GetAsync(name)).Value;
    }

    public async Task CreateResourceGroup(string name)
    {
        var subscription = await GetSubscriptionAsync();
        var resourceGroupData = new ResourceGroupData(_location);
        await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, name, resourceGroupData);
    }

    public async Task DeleteResourceGroup(string name)
    {
        var subscription = await GetSubscriptionAsync();
        var resourceGroup = (await subscription.GetResourceGroups().GetAsync(name)).Value;
        await resourceGroup.DeleteAsync(WaitUntil.Completed);
    }

    internal async Task<bool> ResourceGroupExists(string name)
    {
        var subscription = await GetSubscriptionAsync();
        return await subscription.GetResourceGroups().ExistsAsync(name);
    }

    public async Task<ContainerRegistryInfo> CreateContainerRegistry(string resourceGroupName, string registryName)
    {
        var resourceGroup = await GetResourceGroupResource(resourceGroupName);
        DeploymentLogger.Log($"Creating Container Registry '{registryName}'...");

        var acrData = new ContainerRegistryData(_location, new ContainerRegistrySku(ContainerRegistrySkuName.Basic))
        {
            IsAdminUserEnabled = true
        };
        var acr = await resourceGroup.GetContainerRegistries()
            .CreateOrUpdateAsync(WaitUntil.Completed, registryName, acrData);

        var credentials = await acr.Value.GetCredentialsAsync();
        DeploymentLogger.Log($"Container Registry created: {acr.Value.Data.LoginServer}");

        return new ContainerRegistryInfo(
            acr.Value.Data.LoginServer,
            credentials.Value.Username,
            credentials.Value.Passwords.First().Value);
    }

    public async Task<ApplicationInsights> CreateApplicationInsights(string resourceGroupName, string name)
    {
        var resourceGroup = await GetResourceGroupResource(resourceGroupName);
        var appInsightsData = new ApplicationInsightsComponentData(_location, "web")
        {
            Kind = "web"
        };

        var appInsights = await resourceGroup.GetApplicationInsightsComponents().CreateOrUpdateAsync(
            WaitUntil.Completed, name, appInsightsData);

        var workspaceResource = _armClient.GetOperationalInsightsWorkspaceResource(appInsights.Value.Data.WorkspaceResourceId!);
        var workspace = await workspaceResource.GetAsync();

        var workspaceId = workspace.Value.Data.CustomerId.ToString()!;
        var logsQueryClient = new LogsQueryClient(_credential);

        return new ApplicationInsights(workspaceId, logsQueryClient, appInsights.Value.Data.ConnectionString);
    }

    public async Task<string> CreateContainerAppsEnvironment(string resourceGroupName, string name)
    {
        var resourceGroup = await GetResourceGroupResource(resourceGroupName);
        string environmentName = $"{name}-env";
        DeploymentLogger.Log($"Creating Container Apps Environment '{environmentName}'...");

        var environmentData = new ContainerAppManagedEnvironmentData(_location);
        var environment = await resourceGroup.GetContainerAppManagedEnvironments()
            .CreateOrUpdateAsync(WaitUntil.Completed, environmentName, environmentData);

        DeploymentLogger.Log("Container Apps Environment created");
        return environment.Value.Id.ToString();
    }

    public async Task<ContainerAppInfo> CreateContainerApp(
        string resourceGroupName,
        string environmentId,
        ContainerRegistryInfo registry,
        string name,
        string imageName,
        Dictionary<string, string>? environmentVariables,
        ApplicationInsights applicationInsights,
        string? managedIdentityResourceId)
    {
        var resourceGroup = await GetResourceGroupResource(resourceGroupName);
        DeploymentLogger.Log($"Creating Container App '{name}'...");

        var appiEnvVars = new Dictionary<string, string>
        {
            { "APPLICATIONINSIGHTS_CONNECTION_STRING", applicationInsights.ConnectionString }
        };
        var mergedEnvVars = environmentVariables == null
            ? appiEnvVars
            : appiEnvVars.Concat(environmentVariables).ToDictionary(k => k.Key, v => v.Value);

        var container = new ContainerAppContainer
        {
            Name = name.ToLower(),
            Image = imageName,
            Resources = new AppContainerResources { Cpu = 0.5, Memory = "1Gi" }
        };
        container.Env.Add(new ContainerAppEnvironmentVariable
        {
            Name = "DEPLOYMENT_DATE",
            Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        });
        container.Env.Add(new ContainerAppEnvironmentVariable
        {
            Name = "ASPNETCORE_URLS",
            Value = "http://+:8080"
        });
        foreach (var kvp in mergedEnvVars)
        {
            container.Env.Add(new ContainerAppEnvironmentVariable { Name = kvp.Key, Value = kvp.Value });
        }

        var containerAppData = new ContainerAppData(_location)
        {
            ManagedEnvironmentId = new ResourceIdentifier(environmentId),
            Configuration = new ContainerAppConfiguration
            {
                Ingress = new ContainerAppIngressConfiguration
                {
                    External = true,
                    TargetPort = 8080,
                    Transport = ContainerAppIngressTransportMethod.Auto
                },
                Registries =
                {
                    new ContainerAppRegistryCredentials
                    {
                        Server = registry.LoginServer,
                        Username = registry.Username,
                        PasswordSecretRef = "acr-password"
                    }
                },
                Secrets =
                {
                    new ContainerAppWritableSecret
                    {
                        Name = "acr-password",
                        Value = registry.Password
                    }
                }
            },
            Template = new ContainerAppTemplate
            {
                Containers = { container },
                Scale = new ContainerAppScale { MinReplicas = 1, MaxReplicas = 1 }
            }
        };

        if (managedIdentityResourceId != null)
        {
            containerAppData.Identity = new Azure.ResourceManager.Models.ManagedServiceIdentity(
                Azure.ResourceManager.Models.ManagedServiceIdentityType.UserAssigned);
            containerAppData.Identity.UserAssignedIdentities.Add(
                new ResourceIdentifier(managedIdentityResourceId),
                new Azure.ResourceManager.Models.UserAssignedIdentity());
        }

        var containerApp = await resourceGroup.GetContainerApps()
            .CreateOrUpdateAsync(WaitUntil.Completed, name, containerAppData);

        string fqdn = containerApp.Value.Data.Configuration.Ingress.Fqdn;
        DeploymentLogger.Log("Container App created, waiting for readiness...");

        await WaitForContainerAppToBeReady(fqdn);

        return new ContainerAppInfo(containerApp.Value.Data.Name, fqdn, resourceGroupName, applicationInsights);
    }

    private static async Task WaitForContainerAppToBeReady(string fqdn, int timeoutMinutes = 10, int intervalSeconds = 30)
    {
        string appUrl = $"https://{fqdn}";
        var timeout = TimeSpan.FromMinutes(timeoutMinutes);
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        DeploymentLogger.Log($"Waiting for Container App at {appUrl}...");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                var response = await httpClient.GetAsync(appUrl);

                if (response.IsSuccessStatusCode)
                {
                    DeploymentLogger.Log($"Container App ready (Status: {response.StatusCode})");
                    return;
                }

                DeploymentLogger.Log($"Not ready yet (Status: {response.StatusCode})");
            }
            catch (Exception ex)
            {
                DeploymentLogger.Log($"Not ready yet ({ex.Message})");
            }

            if (stopwatch.Elapsed + interval < timeout)
            {
                await Task.Delay(interval);
            }
        }

        throw new TimeoutException($"Container App at {appUrl} did not become ready within {timeoutMinutes} minutes");
    }
}
