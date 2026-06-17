using Azure.Core;
using AzureIntegration;

namespace AzureTests;

public class ContainerRegistryTests
{
    private static string GenerateRegistryName() =>
        $"testregistry{Guid.NewGuid().ToString("N")[..8]}";

    [Test, Category("LongRunning")]
    public async Task ContainsImage_WhenImageWasNeverPushed_ReturnsFalse()
    {
        var azure = new AzureCloud(location: AzureLocation.EastUS);
        string registryName = GenerateRegistryName();

        await using var registry = await azure.CreateContainerRegistry(
            resourceGroupName: registryName,
            registryName: registryName);

        bool containsImage = await registry.ContainsImage("image-that-was-never-pushed");

        Assert.That(containsImage, Is.False,
            "A freshly created registry with no images pushed should not report that it contains an image.");
    }

    [Test, Category("LongRunning")]
    public async Task ContainsImage_AfterImageIsPushed_ReturnsTrue()
    {
        var azure = new AzureCloud(location: AzureLocation.EastUS);
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
}
