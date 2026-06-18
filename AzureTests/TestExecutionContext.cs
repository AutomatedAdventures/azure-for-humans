using Azure.Core;
using AzureIntegration;

namespace AzureTests;

public abstract class TestExecutionContext : IAsyncDisposable
{
    public abstract AzureCloud Azure();

    public TestExecutionContext Started() => this;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static IEnumerable<TestCaseData> All()
    {
        yield return new TestCaseData(new FakeExecutionContext());
        yield return new TestCaseData(new RealExecutionContext()).SetCategory("LongRunning");
    }
}

public class FakeExecutionContext : TestExecutionContext
{
    public override AzureCloud Azure()
    {
        var arm = new FakeArmClient();
        return new AzureCloud(arm, new FakeDocker(arm), new[] { AzureLocation.WestEurope });
    }
}

public class RealExecutionContext : TestExecutionContext
{
    public override AzureCloud Azure() =>
        new(location: AzureLocation.EastUS);
}
