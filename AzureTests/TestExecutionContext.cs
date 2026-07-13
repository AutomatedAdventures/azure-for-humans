using Azure.Core;
using AzureIntegration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AzureTests;

public abstract class TestExecutionContext : IAsyncDisposable
{
    public abstract AzureCloud Azure();

    public abstract HttpClient HttpClientFor(string url);

    public abstract TimeProvider Time { get; }

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
    private readonly FakeKeyVaultService _keyVault = new();
    private readonly WebApplicationFactory<Program> _projectDependenciesApp = new();
    private readonly WebApplicationFactory<DockerBuildArgsApp.AppMarker> _dockerBuildArgsApp = new();
    private readonly WebApplicationFactory<ContainerApp.AppMarker> _containerApp = new();
    private AzureLocation[] _regions = { AzureLocation.WestEurope };

    public override TimeProvider Time { get; } = new InstantTimeProvider();

    public FakeExecutionContext()
    {
        _ = _projectDependenciesApp.Server;
    }

    public FakeExecutionContext WithRegions(params AzureLocation[] regions)
    {
        _regions = regions;
        return this;
    }

    public FakeExecutionContext WithNoContainerAppCapacityIn(AzureLocation location)
    {
        _arm.ThatHasNoContainerAppCapacityIn(location);
        return this;
    }

    public override AzureCloud Azure() =>
        new(_arm, new FakeAppService(_arm), new FakeFunctionService(_arm), new FakeContainerAppService(_arm), new FakeManagedIdentityService(), _keyVault, new FakeStorageService(), new FakeAppServicePlanService(), new FakeDocker(_arm), _regions);

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
                return Hosted(_dockerBuildArgsApp, app);
            case "TestContainerApp":
                return Hosted(_containerApp, app, services =>
                    services.AddSingleton<ContainerApp.ISecretReader>(new FakeSecretReader(_keyVault)));
            default:
                return new FakeAppHandler(_arm);
        }
    }

    private static HttpMessageHandler Hosted<TEntryPoint>(WebApplicationFactory<TEntryPoint> app, FakeWebApp deployedApp, Action<IServiceCollection>? configureServices = null)
        where TEntryPoint : class =>
        app.WithWebHostBuilder(builder =>
        {
            builder.UseConfiguration(ConfigurationFrom(deployedApp.EnvironmentVariables));
            builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=00000000-0000-0000-0000-000000000000");
            builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(deployedApp.Logs)));
            if (configureServices != null)
                builder.ConfigureServices(configureServices);
        }).Server.CreateHandler();

    private static IConfiguration ConfigurationFrom(Dictionary<string, string> environmentVariables) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(environmentVariables.Select(variable =>
                new KeyValuePair<string, string?>(variable.Key, variable.Value)))
            .Build();

    public override ValueTask DisposeAsync()
    {
        _projectDependenciesApp.Dispose();
        _dockerBuildArgsApp.Dispose();
        _containerApp.Dispose();
        return ValueTask.CompletedTask;
    }
}

public class RealExecutionContext : TestExecutionContext
{
    public override TimeProvider Time => TimeProvider.System;

    public override AzureCloud Azure() =>
        new(location: AzureLocation.EastUS);

    public override HttpClient HttpClientFor(string url) =>
        new() { BaseAddress = BaseAddressFor(url) };
}