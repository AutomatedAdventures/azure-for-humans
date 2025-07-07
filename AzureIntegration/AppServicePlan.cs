using Azure.Core;
using Azure.ResourceManager.AppService;

namespace AzureIntegration;

public class AppServicePlan(AppServicePlanResource appServicePlan)
{
    public string Name => appServicePlan.Data.Name;
    public ResourceIdentifier Id => appServicePlan.Id;
}
