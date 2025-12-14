using AzureIntegration;

namespace AzureTests;

public class ResourceGroupTests
{
    [Test]
    public void GetExistingResourceGroups()
    {
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
        Assert.That(resorceGroups.All(x => x.Name != "testResourceGroup1"));
    }
}
