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
        await azure.DeployAzureFunction(projectDirectory: "AzureFunctionSample", name: "AzureFunctionSample-name999");
        var client = new HttpClient();
        client.BaseAddress = new Uri("https://azurefunctionsample-name.azurewebsites.net");
        var response = await client.GetAsync("api/HttpTrigger");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("Azure Functions Sample test result"));
    }
}
