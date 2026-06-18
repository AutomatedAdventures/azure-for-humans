using Azure.Core;
using AzureIntegration;

namespace AzureTests;

public abstract class TestExecutionContext : IAsyncDisposable
{
    public abstract AzureCloud Azure();

    public abstract HttpClient HttpClientFor(string url);

    public TestExecutionContext Started() => this;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static IEnumerable<TestCaseData> All()
    {
        yield return new TestCaseData(new FakeExecutionContext());
        yield return new TestCaseData(new RealExecutionContext()).SetCategory("RealAzure");
    }
}

public class FakeExecutionContext : TestExecutionContext
{
    private readonly FakeArmClient _arm = new();

    public override AzureCloud Azure() =>
        new(_arm, new FakeAppService(_arm), new FakeFunctionService(_arm), new FakeDocker(_arm), new[] { AzureLocation.WestEurope });

    public override HttpClient HttpClientFor(string url) =>
        new(new FakeAppHandler(_arm)) { BaseAddress = new Uri($"https://{url}") };
}

public class RealExecutionContext : TestExecutionContext
{
    public override AzureCloud Azure() =>
        new(location: AzureLocation.EastUS);

    public override HttpClient HttpClientFor(string url) =>
        new() { BaseAddress = new Uri($"https://{url}") };
}
