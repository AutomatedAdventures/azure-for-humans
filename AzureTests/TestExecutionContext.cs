using Azure.Core;
using AzureIntegration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AzureTests;

public abstract class TestExecutionContext : IAsyncDisposable
{
    public abstract AzureCloud Azure();

    public abstract HttpClient HttpClientFor(string url);

    public TestExecutionContext Started() => this;

    protected static Uri BaseAddressFor(string urlOrHost) =>
        urlOrHost.Contains("://") ? new Uri(urlOrHost) : new Uri($"https://{urlOrHost}");

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
    private readonly WebApplicationFactory<Program> _projectDependenciesApp = new();

    public FakeExecutionContext()
    {
        _ = _projectDependenciesApp.Server;
    }

    public override AzureCloud Azure() =>
        new(_arm, new FakeAppService(_arm), new FakeFunctionService(_arm), new FakeContainerAppService(_arm), new FakeDocker(_arm), new[] { AzureLocation.WestEurope });

    public override HttpClient HttpClientFor(string url)
    {
        var baseAddress = BaseAddressFor(url);
        var app = _arm.WebAppAt(baseAddress.Host);
        var handler = app?.ProjectDirectory == "App"
            ? _projectDependenciesApp.Server.CreateHandler()
            : new FakeAppHandler(_arm);
        return new HttpClient(handler) { BaseAddress = baseAddress };
    }

    public override ValueTask DisposeAsync()
    {
        _projectDependenciesApp.Dispose();
        return ValueTask.CompletedTask;
    }
}

public class RealExecutionContext : TestExecutionContext
{
    public override AzureCloud Azure() =>
        new(location: AzureLocation.EastUS);

    public override HttpClient HttpClientFor(string url) =>
        new() { BaseAddress = BaseAddressFor(url) };
}
