using Azure.Core;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.ApplicationInsights;
using Azure.ResourceManager.OperationalInsights;
using Azure.Monitor.Query;
using Azure;

namespace AzureIntegration;

public record ResourceGroup(ResourceGroupResource Resource, AzureCloud AzureCloud)
{
    public IResourceGroupResource? SeamResource { get; init; }

    public string Name => SeamResource?.Name ?? Resource.Data.Name;

    public AzureLocation Location => SeamResource?.Location ?? Resource.Data.Location;

    public async Task<StorageAccount> CreateStorageAccount(string name)
    {
        var deployment = await AzureCloud.StorageService.Create(this, name);
        return new StorageAccount(deployment.Name, deployment.ConnectionString);
    }

    public async Task<AppServicePlan> CreateAppServicePlanForFunctionApp(string name)
    {
        var deployment = await AzureCloud.AppServicePlanService.CreateForFunctionApp(this, name);
        return new AppServicePlan(deployment.Name, new ResourceIdentifier(deployment.Id));
    }

    public async Task<AppServicePlan> CreateAppServicePlanForWebApp(string name)
    {
        var deployment = await AzureCloud.AppServicePlanService.CreateForWebApp(this, name);
        return new AppServicePlan(deployment.Name, new ResourceIdentifier(deployment.Id));
    }

    public async Task<ApplicationInsights> CreateApplicationInsights(string name)
    {
        var appInsightsData = new ApplicationInsightsComponentData(Resource.Data.Location, "web")
        {
            Kind = "web"
        };

        var appInsights = await Resource.GetApplicationInsightsComponents().CreateOrUpdateAsync(
            WaitUntil.Completed, name, appInsightsData);

        var armClient = AzureCloud.GetArmClient();
        var credential = AzureCloud.GetCredential();
        
        var workspaceResource = armClient.GetOperationalInsightsWorkspaceResource(appInsights.Value.Data.WorkspaceResourceId!);
        var workspace = await workspaceResource.GetAsync();
        
        var workspaceId = workspace.Value.Data.CustomerId.ToString();
        var logsQueryClient = new LogsQueryClient(credential);
        var connectionString = appInsights.Value.Data.ConnectionString ?? string.Empty;

        return new ApplicationInsights(workspaceId!, logsQueryClient, connectionString);
    }
}
