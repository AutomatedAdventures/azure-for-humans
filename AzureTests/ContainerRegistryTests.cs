using Azure.Core;
using AzureIntegration;

namespace AzureTests;

public class ContainerRegistryTests
{
    private static string GenerateRegistryName() =>
        $"testregistry{Guid.NewGuid().ToString("N")[..8]}";

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task ContainsImage_WhenImageWasNeverPushed_ReturnsFalse(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string registryName = GenerateRegistryName();

        await using var registry = await azure.CreateContainerRegistry(
            resourceGroupName: registryName,
            registryName: registryName);

        bool containsImage = await registry.ContainsImage("image-that-was-never-pushed");

        Assert.That(containsImage, Is.False,
            "A freshly created registry with no images pushed should not report that it contains an image.");
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task ContainsImage_AfterImageIsPushed_ReturnsTrue(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string registryName = GenerateRegistryName();
        string imageName = "the-simplest-image";

        await using var registry = await azure.CreateContainerRegistry(
            resourceGroupName: registryName,
            registryName: registryName);
        await registry.PushImage(imageName);

        bool containsImage = await registry.ContainsImage(imageName);

        Assert.That(containsImage, Is.True,
            "After pushing an image, the registry should report that it contains it.");
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task Exists_WhenRegistryWasCreated_ReturnsTrue(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string registryName = GenerateRegistryName();

        await using var registry = await azure.CreateContainerRegistry(
            resourceGroupName: registryName,
            registryName: registryName);

        bool exists = await registry.Exists();

        Assert.That(exists, Is.True,
            "A registry that was just created should report that it exists.");
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task Exists_AfterRegistryWasDeleted_ReturnsFalse(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string registryName = GenerateRegistryName();

        var registry = await azure.CreateContainerRegistry(
            resourceGroupName: registryName,
            registryName: registryName);
        await registry.DisposeAsync();

        bool exists = await registry.Exists();

        Assert.That(exists, Is.False,
            "A registry whose resources were deleted should report that it no longer exists.");
    }
}
