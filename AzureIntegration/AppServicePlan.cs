using Azure.Core;
using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AppServicePlan
{
    private readonly AppServicePlanResource _appServicePlan;

    public AppServicePlan(AppServicePlanResource appServicePlan)
    {
        _appServicePlan = appServicePlan;
    }

    public string Name => _appServicePlan.Data.Name;
    public ResourceIdentifier Id => _appServicePlan.Id;
}
