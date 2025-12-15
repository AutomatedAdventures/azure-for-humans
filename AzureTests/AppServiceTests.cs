using System.Net;
using AzureIntegration;

namespace AzureTests;

public class AppServiceTests
{
    private static string GenerateAppServiceName() =>
        $"testappservice-{Guid.NewGuid().ToString("N")[..8]}";

    [Test]
    public async Task DeployAppService_WhenDeploymentFails_CleansUpResources()
    {
        var azure = new AzureCloud();
        string appServiceName = GenerateAppServiceName();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await azure.DeployAppService(
                projectDirectory: "TestAppServiceWithBuildError",
                name: appServiceName));
        
        Assert.That(exception!.Message, Does.Contain("Project build failed"));
        Assert.That(exception.Message, Does.Contain("TestAppServiceWithBuildError"));
        
        bool resourceGroupExists = await azure.ResourceGroupExists(appServiceName);
        Assert.That(resourceGroupExists, Is.False, 
            $"Resource group '{appServiceName}' should have been cleaned up after deployment failure");
    }

    [Test]
    public async Task DeployAppService()
    {
        var azure = new AzureCloud();
        string appServiceName = GenerateAppServiceName();
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
        string appServiceName = GenerateAppServiceName();
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
