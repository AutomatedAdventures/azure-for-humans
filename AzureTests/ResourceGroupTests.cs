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

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task CreateAndDeleteResourceGroup(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string resourceGroupName = $"test-rg-{Guid.NewGuid().ToString("N")[..8]}";

        await azure.CreateResourceGroup(name: resourceGroupName);
        var resourceGroups = await azure.GetResourceGroups();
        Assert.That(resourceGroups.Any(x => x.Name == resourceGroupName));

        await azure.DeleteResourceGroup(name: resourceGroupName);
        resourceGroups = await azure.GetResourceGroups();
        Assert.That(resourceGroups.All(x => x.Name != resourceGroupName));
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
