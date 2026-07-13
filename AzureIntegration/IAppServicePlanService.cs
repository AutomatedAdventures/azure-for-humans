using Azure;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;

namespace AzureIntegration;

public interface IAppServicePlanService
{
    Task<AppServicePlanDeployment> CreateForFunctionApp(ResourceGroup resourceGroup, string name);
    Task<AppServicePlanDeployment> CreateForWebApp(ResourceGroup resourceGroup, string name);
}

public record AppServicePlanDeployment(string Name, string Id);

public class RealAppServicePlanService : IAppServicePlanService
{
    public Task<AppServicePlanDeployment> CreateForFunctionApp(ResourceGroup resourceGroup, string name) =>
        Create(resourceGroup, name, new AppServiceSkuDescription { Name = "Y1", Tier = "Dynamic" }, kind: "FunctionApp");

    public Task<AppServicePlanDeployment> CreateForWebApp(ResourceGroup resourceGroup, string name) =>
        Create(resourceGroup, name, new AppServiceSkuDescription { Name = "F1", Tier = "Free" }, kind: "linux");

    private static async Task<AppServicePlanDeployment> Create(ResourceGroup resourceGroup, string name, AppServiceSkuDescription sku, string kind)
    {
        var appServicePlanData = new AppServicePlanData(resourceGroup.Resource.Data.Location)
        {
            Sku = sku,
            Kind = kind,
            IsReserved = true
        };

        var appServicePlan = await resourceGroup.Resource.GetAppServicePlans().CreateOrUpdateAsync(
            WaitUntil.Completed, name, appServicePlanData);

        return new AppServicePlanDeployment(appServicePlan.Value.Data.Name, appServicePlan.Value.Id.ToString());
    }
}

public class FailingAppServicePlanService : IAppServicePlanService
{
    public Task<AppServicePlanDeployment> CreateForFunctionApp(ResourceGroup resourceGroup, string name) =>
        throw new NotSupportedException("This AzureCloud was not configured to create app service plans.");

    public Task<AppServicePlanDeployment> CreateForWebApp(ResourceGroup resourceGroup, string name) =>
        throw new NotSupportedException("This AzureCloud was not configured to create app service plans.");
}
