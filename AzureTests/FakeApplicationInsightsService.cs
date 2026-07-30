using AzureIntegration;

namespace AzureTests;

public class FakeApplicationInsightsService : IApplicationInsightsService
{
    public Task<ApplicationInsightsDeployment> Create(ResourceGroup resourceGroup, string name) =>
        Task.FromResult(new ApplicationInsightsDeployment(
            ConnectionString: "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            new EmptyApplicationLogs()));
}

internal class EmptyApplicationLogs : IApplicationLogs
{
    public IEnumerable<string> GetLogs() => Enumerable.Empty<string>();

    public IEnumerable<string> GetLogs(string kqlQuery) => Enumerable.Empty<string>();
}
