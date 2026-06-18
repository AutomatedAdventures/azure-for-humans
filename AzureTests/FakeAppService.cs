using System.Net;
using AzureIntegration;

namespace AzureTests;

internal class FakeWebApp(string projectDirectory, Dictionary<string, string> environmentVariables)
{
    public string ProjectDirectory => projectDirectory;
    public Dictionary<string, string> EnvironmentVariables => environmentVariables;
    public List<string> Logs { get; } = new();
}

public class FakeAppService(FakeArmClient arm) : IAppService
{
    public Task<string> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string projectDirectory,
        Dictionary<string, string>? environmentVariables,
        string zipFilePath)
    {
        string host = $"{name.ToLower()}.azurewebsites.net";
        arm.RecordWebApp(host, projectDirectory, environmentVariables);
        return Task.FromResult(host);
    }
}

public class FakeFunctionService(FakeArmClient arm) : IFunctionService
{
    public Task<FunctionDeployment> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string projectDirectory,
        Dictionary<string, string>? environmentVariables,
        string zipFilePath)
    {
        string host = $"{name.ToLower()}.azurewebsites.net";
        var app = arm.RecordWebApp(host, projectDirectory, environmentVariables);
        return Task.FromResult(new FunctionDeployment(host, new FakeApplicationLogs(app)));
    }
}

public class FakeContainerAppService(FakeArmClient arm) : IContainerAppService
{
    public Task<ContainerAppDeployment> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string imageName,
        IContainerRegistryResource acr,
        Dictionary<string, string>? environmentVariables,
        string? managedIdentityResourceId)
    {
        string fqdn = $"{name.ToLower()}.fake.azurecontainerapps.io";
        var app = arm.RecordWebApp(fqdn, imageName.Split('/').Last().Split(':').First(), environmentVariables);
        return Task.FromResult(new ContainerAppDeployment(fqdn, new FakeApplicationLogs(app)));
    }
}

internal class FakeApplicationLogs(FakeWebApp app) : IApplicationLogs
{
    public IEnumerable<string> GetLogs() => app.Logs;

    public IEnumerable<string> GetLogs(string kqlQuery) =>
        app.Logs.Select(log => $"custom-kql:{log}");
}

public class FakeAppHandler(FakeArmClient arm) : HttpMessageHandler
{
    private const string TriggerLog = "C# HTTP trigger function processed a request.";

    private static readonly Dictionary<string, string> TriggerResponses = new()
    {
        ["AzureFunctionSample"] = "Azure Functions Sample test result"
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var app = arm.WebAppAt(request.RequestUri!.Host);
        if (app is null)
        {
            return NotFound();
        }

        string path = request.RequestUri.AbsolutePath.Trim('/');

        if (path == "")
        {
            return Ok($"{app.ProjectDirectory} deployment successful!");
        }

        if (path.EndsWith("HttpTrigger"))
        {
            app.Logs.Add(TriggerLog);
            return Ok(TriggerResponses[app.ProjectDirectory]);
        }

        int variableIndex = path.IndexOf("variable/", StringComparison.Ordinal);
        if (variableIndex >= 0 &&
            app.EnvironmentVariables.TryGetValue(path[(variableIndex + "variable/".Length)..], out var value))
        {
            return Ok(value);
        }

        return NotFound();
    }

    private static Task<HttpResponseMessage> Ok(string content) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });

    private static Task<HttpResponseMessage> NotFound() =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}
