using AzureIntegration;

namespace AzureTests;

public class ApplicationInsightsTests
{
    // MICRO-TEST (scaffolding): drives the extraction of the application insights SDK
    // into IApplicationInsightsService. It does not assert user-visible value on its own.
    // Revisit: replace with a dual test that reads application logs through a real scenario.
    [Category("MicroTest")]
    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task ACreatedApplicationInsights_ExposesAConnectionString(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string name = $"appi{Guid.NewGuid().ToString("N")[..8]}";

        var resourceGroup = await azure.CreateResourceGroup(name);
        try
        {
            var applicationInsights = await resourceGroup.CreateApplicationInsights(name);

            Assert.That(applicationInsights.ConnectionString, Is.Not.Empty,
                "Application insights should expose a connection string for telemetry.");
        }
        finally
        {
            await azure.DeleteResourceGroup(name);
        }
    }
}
