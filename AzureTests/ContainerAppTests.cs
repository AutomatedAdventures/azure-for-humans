using System.Net;
using AzureIntegration;

namespace AzureTests;

public class ContainerAppTests
{
    private static string GenerateContainerAppName() =>
        $"testcontainerapp-{Guid.NewGuid().ToString("N")[..8]}";

    [Test]
    public async Task DeployContainerApp_WhenDeploymentFails_CleansUpResources()
    {
        var azure = new AzureCloud();
        string containerAppName = GenerateContainerAppName();

        Assert.ThrowsAsync<Exception>(async () =>
            await azure.DeployContainerApp(
                projectDirectory: "TestContainerAppWithBrokenDockerfile",
                name: containerAppName));
        
        bool resourceGroupExists = await azure.ResourceGroupExists(containerAppName);
        Assert.That(resourceGroupExists, Is.False, 
            $"Resource group '{containerAppName}' should have been cleaned up after deployment failure");
    }

    [Test]
    public async Task DeployContainerApp()
    {
        var azure = new AzureCloud();
        string containerAppName = GenerateContainerAppName();
        var envVars = new Dictionary<string, string>
                      {
                          { "MY_ENV_VAR1", "value1" },
                          { "MY_ENV_VAR2", "value2" }
                      };
        
        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: containerAppName,
            environmentVariables: envVars);

        await AssertResourceGroupExists(azure, containerAppName);
        await AssertContainerAppRespondsWithExpectedContent(containerApp);
        await AssertEnvironmentVariablesAreAccessible(containerApp, envVars);
    }

    private static async Task AssertResourceGroupExists(AzureCloud azure, string containerAppName)
    {
        bool resourceGroupExists = await azure.ResourceGroupExists(containerAppName);
        Assert.That(resourceGroupExists, Is.True, 
            $"Resource group '{containerAppName}' should exist after successful deployment");
    }

    private static async Task AssertContainerAppRespondsWithExpectedContent(AzureContainerApp containerApp)
    {
        using var client = new HttpClient { BaseAddress = new Uri(containerApp.Url) };
        var response = await client.GetAsync("/");
        
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("TestContainerApp deployment successful!"));
    }

    private static async Task AssertEnvironmentVariablesAreAccessible(
        AzureContainerApp containerApp, 
        Dictionary<string, string> envVars)
    {
        using var client = new HttpClient { BaseAddress = new Uri(containerApp.Url) };
        
        foreach (var (key, expectedValue) in envVars)
        {
            var response = await client.GetAsync($"/variable/{key}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Variable {key} not found");
            string value = await response.Content.ReadAsStringAsync();
            Assert.That(value.Trim('"'), Is.EqualTo(expectedValue), $"Variable {key} value mismatch");
        }
    }
}
