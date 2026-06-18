using Azure;
using Azure.ResourceManager.ContainerRegistry;
using Azure.ResourceManager.ContainerRegistry.Models;

namespace AzureIntegration;

public class ContainerRegistry(
    string resourceId,
    string loginServer,
    string username,
    string password,
    string resourceGroupName,
    AzureCloud azureCloud,
    IDocker docker) : IAsyncDisposable
{
    public string ResourceId => resourceId;
    public string LoginServer => loginServer;
    public string Username => username;
    public string Password => password;

    public Task<bool> Exists() => azureCloud.ContainerRegistryExists(resourceId);

    public static async Task<ContainerRegistry> CreateAsync(
        AzureCloud azureCloud,
        string resourceGroupName,
        string registryName)
    {
        DeploymentLogger.Log($"Creating container registry '{registryName}' in '{resourceGroupName}'...");
        var resourceGroup = await azureCloud.CreateResourceGroup(resourceGroupName);

        var acrData = new ContainerRegistryData(azureCloud.Location, new ContainerRegistrySku(ContainerRegistrySkuName.Basic))
        {
            IsAdminUserEnabled = true
        };

        var acr = await resourceGroup.SeamResource!.GetContainerRegistries()
            .CreateOrUpdateAsync(WaitUntil.Completed, registryName, acrData);

        var credentials = await acr.GetCredentialsAsync();

        DeploymentLogger.Log($"Container Registry created: {acr.LoginServer}");

        return new ContainerRegistry(
            acr.Id,
            acr.LoginServer,
            credentials.Username,
            credentials.Password,
            resourceGroupName,
            azureCloud,
            azureCloud.Docker);
    }

    public async Task<bool> ContainsImage(string imageName)
    {
        try
        {
            DeploymentLogger.Log($"Checking if registry '{LoginServer}' contains image '{imageName}'...");

            await docker.Login(LoginServer, Username, Password);
            string fullImageName = $"{LoginServer}/{imageName}:latest";
            return await docker.ManifestExists(fullImageName);
        }
        catch (Exception ex)
        {
            DeploymentLogger.LogError($"Error checking if registry contains image: {ex.Message}");
            return false;
        }
    }

    public async Task PushImage(string imageName)
    {
        DeploymentLogger.Log($"Pushing image '{imageName}' to registry '{LoginServer}'...");

        await docker.Login(LoginServer, Username, Password);
        await docker.Pull("hello-world");

        string targetImage = $"{LoginServer}/{imageName}:latest";
        await docker.Tag("hello-world", targetImage);
        await docker.Push(targetImage);

        DeploymentLogger.Log($"Image '{imageName}' pushed to registry");
    }

    public async ValueTask DisposeAsync()
    {
        await azureCloud.DeleteResourceGroup(resourceGroupName);
    }
}

