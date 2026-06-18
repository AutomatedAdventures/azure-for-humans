using System.Net;
using AzureIntegration;

namespace AzureTests;

internal record FakeWebApp(string ProjectDirectory, Dictionary<string, string> EnvironmentVariables);

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

public class FakeAppHandler(FakeArmClient arm) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var app = arm.WebAppAt(request.RequestUri!.Host);
        if (app is null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        string path = request.RequestUri.AbsolutePath;
        if (path == "/")
        {
            return Ok($"{app.ProjectDirectory} deployment successful!");
        }

        if (path.StartsWith("/variable/") &&
            app.EnvironmentVariables.TryGetValue(path["/variable/".Length..], out var value))
        {
            return Ok(value);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Ok(string content) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
}
