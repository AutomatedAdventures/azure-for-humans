using System.Net;
using AzureIntegration;

namespace AzureTests;

public class ContainerAppTests
{
    [Test]
    public async Task DeployContainerApp()
    {
        var azure = new AzureCloud();
        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string containerAppName = $"testcontainerapp-{uniqueId}";
        
        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: containerAppName);
        
        using var client = new HttpClient();
        client.BaseAddress = new Uri(containerApp.Url);
        var response = await client.GetAsync("/");
        
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("TestContainerApp deployment successful!"));
    }

    [Test]
    public async Task DeployContainerApp_WithEnvironmentVariables()
    {
        var azure = new AzureCloud();
        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string containerAppName = $"testcontainerapp-{uniqueId}";
        var envVars = new Dictionary<string, string>
                      {
                          { "MY_ENV_VAR1", "value1" },
                          { "MY_ENV_VAR2", "value2" }
                      };
        
        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: containerAppName,
            environmentVariables: envVars);
        
        using var client = new HttpClient();
        client.BaseAddress = new Uri(containerApp.Url);
        foreach (var kvp in envVars)
        {
            var response = await client.GetAsync($"/variable/{kvp.Key}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Variable {kvp.Key} not found");
            string value = await response.Content.ReadAsStringAsync();
            Assert.That(value.Trim('"'), Is.EqualTo(kvp.Value), $"Variable {kvp.Key} value mismatch");
        }
    }
}
