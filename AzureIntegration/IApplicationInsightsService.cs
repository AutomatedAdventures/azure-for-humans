using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.ResourceManager;
using Azure.ResourceManager.ApplicationInsights;
using Azure.ResourceManager.OperationalInsights;

namespace AzureIntegration;

public interface IApplicationInsightsService
{
    Task<ApplicationInsightsDeployment> Create(ResourceGroup resourceGroup, string name);
}

public record ApplicationInsightsDeployment(string ConnectionString, IApplicationLogs Logs);

public class RealApplicationInsightsService(TokenCredential credentials) : IApplicationInsightsService
{
    public async Task<ApplicationInsightsDeployment> Create(ResourceGroup resourceGroup, string name)
    {
        var appInsightsData = new ApplicationInsightsComponentData(resourceGroup.Resource.Data.Location, "web")
        {
            Kind = "web"
        };

        var appInsights = await resourceGroup.Resource.GetApplicationInsightsComponents().CreateOrUpdateAsync(
            WaitUntil.Completed, name, appInsightsData);

        var armClient = new ArmClient(credentials);
        var workspaceResource = armClient.GetOperationalInsightsWorkspaceResource(appInsights.Value.Data.WorkspaceResourceId!);
        var workspace = await workspaceResource.GetAsync();

        var workspaceId = workspace.Value.Data.CustomerId.ToString();
        var logsQueryClient = new LogsQueryClient(credentials);
        var connectionString = appInsights.Value.Data.ConnectionString ?? string.Empty;

        var logs = new ApplicationInsights(workspaceId, logsQueryClient, connectionString);
        return new ApplicationInsightsDeployment(connectionString, logs);
    }
}

public class FailingApplicationInsightsService : IApplicationInsightsService
{
    public Task<ApplicationInsightsDeployment> Create(ResourceGroup resourceGroup, string name) =>
        throw new NotSupportedException("This AzureCloud was not configured to create application insights.");
}
