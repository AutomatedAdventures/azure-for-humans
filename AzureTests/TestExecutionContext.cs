using Azure.Core;
using AzureIntegration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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
    private readonly WebApplicationFactory<DockerBuildArgsApp.AppMarker> _dockerBuildArgsApp = new();

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
        var handler = HandlerFor(app);
        return new HttpClient(handler) { BaseAddress = baseAddress };
    }

    private HttpMessageHandler HandlerFor(FakeWebApp? app)
    {
        switch (app?.ProjectDirectory)
        {
            case "App":
                return _projectDependenciesApp.Server.CreateHandler();
            case "TestContainerAppWithDockerBuildArgs":
                return _dockerBuildArgsApp.WithWebHostBuilder(builder =>
                    builder.UseConfiguration(ConfigurationFrom(app.EnvironmentVariables))).Server.CreateHandler();
            default:
                return new FakeAppHandler(_arm);
        }
    }

    private static IConfiguration ConfigurationFrom(Dictionary<string, string> environmentVariables) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(environmentVariables.Select(variable =>
                new KeyValuePair<string, string?>(variable.Key, variable.Value)))
            .Build();

    public override ValueTask DisposeAsync()
    {
        _projectDependenciesApp.Dispose();
        _dockerBuildArgsApp.Dispose();
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
