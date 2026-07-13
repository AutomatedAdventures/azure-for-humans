using Azure.Core;
using Azure.ResourceManager.Resources;

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

    public Task<ApplicationInsightsDeployment> CreateApplicationInsights(string name) =>
        AzureCloud.ApplicationInsightsService.Create(this, name);
}
