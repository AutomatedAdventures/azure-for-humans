using AzureIntegration;

namespace AzureTests;

public class AppServicePlanTests
{
    // MICRO-TEST (scaffolding): drives the extraction of the app service plan SDK
    // into IAppServicePlanService. It does not assert user-visible value on its own.
    // Revisit: replace with a dual web-app deployment test that exercises the plan
    // through a real user scenario.
    [Category("MicroTest")]
    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task ACreatedAppServicePlan_ExposesAnIdThatNamesThePlan(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string name = $"plan{Guid.NewGuid().ToString("N")[..8]}";

        var resourceGroup = await azure.CreateResourceGroup(name);
        try
        {
            var appServicePlan = await resourceGroup.CreateAppServicePlanForWebApp(name);

            Assert.That(appServicePlan.Id.ToString(), Does.Contain(appServicePlan.Name),
                "An app service plan's id should reference the plan it identifies.");
        }
        finally
        {
            await azure.DeleteResourceGroup(name);
        }
    }
}
