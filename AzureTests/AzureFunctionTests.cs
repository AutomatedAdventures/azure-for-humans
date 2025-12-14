using System.Net;
using AzureIntegration;

namespace AzureTests;

public class AzureFunctionTests
{
    [Test]
    public async Task DeployAzureFunction()
    {
        var azure = new AzureCloud();
        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string functionName = $"AzureFunctionSample-{uniqueId}";

        await using var function = await azure.DeployAzureFunction(projectDirectory: "AzureFunctionSample", name: functionName);
        using var client = new HttpClient();
        client.BaseAddress = new Uri($"https://{functionName.ToLower()}.azurewebsites.net");
        var response = await client.GetAsync("api/HttpTrigger");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("Azure Functions Sample test result"));
    }

    [Test]
    public async Task DeployAzureFunction_WithEnvironmentVariables()
    {
        var azure = new AzureCloud();
        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string functionName = $"TestAzureFunction-{uniqueId}";
        var envVars = new Dictionary<string, string>
                      {
                          { "MY_ENV_VAR1", "value1" },
                          { "MY_ENV_VAR2", "value2" }
                      };
        await using var function =
            await azure.DeployAzureFunction(
                projectDirectory: "TestAzureFunction",
                name: functionName,
                environmentVariables: envVars);
        using var client = new HttpClient();
        client.BaseAddress = new Uri($"https://{functionName.ToLower()}.azurewebsites.net");
        foreach (var kvp in envVars)
        {
            var response = await client.GetAsync($"api/variable/{kvp.Key}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Variable {kvp.Key} not found");
            string value = await response.Content.ReadAsStringAsync();
            Assert.That(value.Trim('"'), Is.EqualTo(kvp.Value), $"Variable {kvp.Key} value mismatch");
        }
    }
}
