using Azure;
using Azure.Core;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;

namespace AzureIntegration;

public record ContainerAppDeployment(string Fqdn, IApplicationLogs Logs);

public interface IContainerAppService
{
    Task<ContainerAppDeployment> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string imageName,
        IContainerRegistryResource acr,
        Dictionary<string, string>? environmentVariables,
        string? managedIdentityResourceId);
}

public class RealContainerAppService : IContainerAppService
{
    public async Task<ContainerAppDeployment> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string imageName,
        IContainerRegistryResource acr,
        Dictionary<string, string>? environmentVariables,
        string? managedIdentityResourceId)
    {
        var applicationInsights = await resourceGroup.CreateApplicationInsights(name);
        var environment = await CreateContainerAppsEnvironment(resourceGroup, name);

        var location = resourceGroup.Resource.Data.Location;
        var credentials = await acr.GetCredentialsAsync();
        var appiEnvVars = new Dictionary<string, string>
        {
            { "APPLICATIONINSIGHTS_CONNECTION_STRING", applicationInsights.ConnectionString }
        };
        var mergedEnvVars = environmentVariables == null
            ? appiEnvVars
            : appiEnvVars.Concat(environmentVariables).ToDictionary(k => k.Key, v => v.Value);
        var container = BuildContainer(name, imageName, mergedEnvVars);

        var containerAppData = new ContainerAppData(location)
        {
            ManagedEnvironmentId = environment.Id,
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
                        Server = acr.LoginServer,
                        Username = credentials.Username,
                        PasswordSecretRef = "acr-password"
                    }
                },
                Secrets =
                {
                    new ContainerAppWritableSecret
                    {
                        Name = "acr-password",
                        Value = credentials.Password
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

        var containerApp = await resourceGroup.Resource.GetContainerApps()
            .CreateOrUpdateAsync(WaitUntil.Completed, name, containerAppData);

        string fqdn = containerApp.Value.Data.Configuration.Ingress.Fqdn;
        DeploymentLogger.Log("Container App created, waiting for readiness...");

        await WaitForContainerAppToBeReady(fqdn);

        return new ContainerAppDeployment(fqdn, applicationInsights);
    }

    private static async Task<ContainerAppManagedEnvironmentResource> CreateContainerAppsEnvironment(ResourceGroup resourceGroup, string name)
    {
        string environmentName = $"{name}-env";
        DeploymentLogger.Log($"Creating Container Apps Environment '{environmentName}'...");

        var environmentData = new ContainerAppManagedEnvironmentData(resourceGroup.Resource.Data.Location);
        var environment = await resourceGroup.Resource.GetContainerAppManagedEnvironments()
            .CreateOrUpdateAsync(WaitUntil.Completed, environmentName, environmentData);

        DeploymentLogger.Log("Container Apps Environment created");
        return environment.Value;
    }

    private static ContainerAppContainer BuildContainer(
        string name,
        string imageName,
        Dictionary<string, string>? environmentVariables)
    {
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

        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                container.Env.Add(new ContainerAppEnvironmentVariable { Name = kvp.Key, Value = kvp.Value });
            }
        }

        return container;
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

public class FailingContainerAppService : IContainerAppService
{
    public Task<ContainerAppDeployment> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string imageName,
        IContainerRegistryResource acr,
        Dictionary<string, string>? environmentVariables,
        string? managedIdentityResourceId) =>
        throw new InvalidOperationException("This AzureCloud was not configured with a container app service.");
}
