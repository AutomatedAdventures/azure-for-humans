using AzureIntegration;

namespace AzureTests;

public class ResourceGroupTests
{
    [Test, Category("RealAzure")]
    public void GetExistingResourceGroups()
    {
        var azure = new AzureCloud();
        var resourceGroups = azure.GetResourceGroups().Result;
        Assert.That(resourceGroups.Count, Is.GreaterThan(0));
    }

    [Test, Category("RealAzure")]
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

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task ResourceGroupExists_ReturnsTrueForExistingGroup(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string resourceGroupName = $"test-rg-exists-{Guid.NewGuid().ToString("N")[..8]}";

        await azure.CreateResourceGroup(name: resourceGroupName);
        
        bool exists = await azure.ResourceGroupExists(resourceGroupName);
        Assert.That(exists, Is.True);

        await azure.DeleteResourceGroup(name: resourceGroupName);
        
        exists = await azure.ResourceGroupExists(resourceGroupName);
        Assert.That(exists, Is.False);
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task ResourceGroupExists_ReturnsFalseForNonExistingGroup(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string resourceGroupName = $"non-existing-rg-{Guid.NewGuid().ToString("N")[..8]}";

        bool exists = await azure.ResourceGroupExists(resourceGroupName);
        
        Assert.That(exists, Is.False);
    }
}
