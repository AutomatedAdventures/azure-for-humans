using System.Net;
using AzureIntegration;

namespace AzureTests;

public class Tests
{
    [Test]
    public void CheckAzureEnvironmentVariablesAreDefined(){
        Assert.That(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"), Is.Not.Null.And.Not.Empty);
        Assert.That(Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET"), Is.Not.Null.And.Not.Empty);
        Assert.That(Environment.GetEnvironmentVariable("AZURE_TENANT_ID"), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GetExistingResourceGroups(){
        var azure = new AzureCloud();
        var resourceGroups = azure.GetResourceGroups().Result;
        Assert.That(resourceGroups.Count, Is.GreaterThan(0));
    }

    [Test]
    public async Task CreateAndDeleteResourceGroup()
    {
        var azure = new AzureCloud();
        await azure.CreateResourceGroup(name: "testResourceGroup1");
        var resorceGroups = await azure.GetResourceGroups();
        Assert.That(resorceGroups.Any(x => x.Name == "testResourceGroup1"));
        await azure.DeleteResourceGroup(name: "testResourceGroup1");
        resorceGroups = await azure.GetResourceGroups();
        Assert.That(!resorceGroups.Any(x => x.Name == "testResourceGroup1"));
    }

    [Test]
    public async Task DeployAzureFunction(){
        var azure = new AzureCloud();
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var functionName = $"AzureFunctionSample-{uniqueId}";
        await using (var function = await azure.DeployAzureFunction(projectDirectory: "AzureFunctionSample", name: functionName))
        {
            var client = new HttpClient();
            client.BaseAddress = new Uri($"https://{functionName.ToLower()}.azurewebsites.net");
            var response = await client.GetAsync("api/HttpTrigger");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var content = await response.Content.ReadAsStringAsync();
            Assert.That(content, Is.EqualTo("Azure Functions Sample test result"));
        }
    }

    [Test]
    public async Task DeployAzureFunction_WithEnvironmentVariables()
    {
        var azure = new AzureCloud();
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var functionName = $"TestAzureFunction-{uniqueId}";
        var envVars = new Dictionary<string, string>
        {
            {"MY_ENV_VAR1", "value1"},
            {"MY_ENV_VAR2", "value2"}
        };
        await using (var function = await azure.DeployAzureFunction(
            projectDirectory: "TestAzureFunction",
            name: functionName,
            environmentVariables: envVars))
        {
            var client = new HttpClient();
            client.BaseAddress = new Uri($"https://{functionName.ToLower()}.azurewebsites.net");
            foreach (var kvp in envVars)
            {
                var response = await client.GetAsync($"api/variable/{kvp.Key}");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Variable {kvp.Key} not found");
                var value = await response.Content.ReadAsStringAsync();
                Assert.That(value.Trim('"'), Is.EqualTo(kvp.Value), $"Variable {kvp.Key} value mismatch");
            }
        }
    }

    [Test]
    public async Task DeployAppService()
    {
        var azure = new AzureCloud();
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var appServiceName = $"TestAppService-{uniqueId}";
        await using (var app = await azure.DeployAppService(projectDirectory: "TestAppService", name: appServiceName))
        {
            var client = new HttpClient();
            client.BaseAddress = new Uri($"https://{appServiceName.ToLower()}.azurewebsites.net");
            var response = await client.GetAsync("/");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var content = await response.Content.ReadAsStringAsync();
            Assert.That(content, Is.EqualTo("TestAppService deployment successful!"));
        }
    }

    [Test]
    public async Task DeployAppService_WithEnvironmentVariables()
    {
        var azure = new AzureCloud();
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var appServiceName = $"TestAppService-{uniqueId}";
        var envVars = new Dictionary<string, string>
        {
            {"MY_ENV_VAR1", "value1"},
            {"MY_ENV_VAR2", "value2"}
        };
        await using (var app = await azure.DeployAppService(
            projectDirectory: "TestAppService",
            name: appServiceName,
            environmentVariables: envVars))
        {
            var client = new HttpClient();
            client.BaseAddress = new Uri($"https://{appServiceName.ToLower()}.azurewebsites.net");
            foreach (var kvp in envVars)
            {
                var response = await client.GetAsync($"/variable/{kvp.Key}");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Variable {kvp.Key} not found");
                var value = await response.Content.ReadAsStringAsync();
                Assert.That(value.Trim('"'), Is.EqualTo(kvp.Value), $"Variable {kvp.Key} value mismatch");
            }
        }
    }
}
