using System.Net;
using AzureIntegration;

namespace AzureTests;

public class AppServiceTests
{
    [Test]
    public async Task DeployAppService()
    {
        var azure = new AzureCloud();
        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string appServiceName = $"TestAppService-{uniqueId}";
        await using var app = await azure.DeployAppService(projectDirectory: "TestAppService", name: appServiceName);
        using var client = new HttpClient();
        client.BaseAddress = new Uri($"https://{appServiceName.ToLower()}.azurewebsites.net");
        var response = await client.GetAsync("/");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("TestAppService deployment successful!"));
    }

    [Test]
    public async Task DeployAppService_WithEnvironmentVariables()
    {
        var azure = new AzureCloud();
        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string appServiceName = $"TestAppService-{uniqueId}";
        var envVars = new Dictionary<string, string>
                      {
                          { "MY_ENV_VAR1", "value1" },
                          { "MY_ENV_VAR2", "value2" }
                      };
        await using var app =
            await azure.DeployAppService(
                projectDirectory: "TestAppService",
                name: appServiceName,
                environmentVariables: envVars);
        using var client = new HttpClient();
        client.BaseAddress = new Uri($"https://{appServiceName.ToLower()}.azurewebsites.net");
        foreach (var kvp in envVars)
        {
            var response = await client.GetAsync($"/variable/{kvp.Key}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Variable {kvp.Key} not found");
            string value = await response.Content.ReadAsStringAsync();
            Assert.That(value.Trim('"'), Is.EqualTo(kvp.Value), $"Variable {kvp.Key} value mismatch");
        }
    }
}
