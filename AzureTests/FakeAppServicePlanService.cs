using AzureIntegration;

namespace AzureTests;

public class FakeAppServicePlanService : IAppServicePlanService
{
    public Task<AppServicePlanDeployment> CreateForFunctionApp(ResourceGroup resourceGroup, string name) =>
        Task.FromResult(new AppServicePlanDeployment(name, IdFor(name)));

    public Task<AppServicePlanDeployment> CreateForWebApp(ResourceGroup resourceGroup, string name) =>
        Task.FromResult(new AppServicePlanDeployment(name, IdFor(name)));

    private static string IdFor(string name) =>
        $"/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.Web/serverfarms/{name}";
}
