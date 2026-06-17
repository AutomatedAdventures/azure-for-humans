using System.Diagnostics;
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
    AzureCloud azureCloud) : IAsyncDisposable
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
        
        var acr = await resourceGroup.Resource.GetContainerRegistries()
            .CreateOrUpdateAsync(WaitUntil.Completed, registryName, acrData);
        
        var credentials = await acr.Value.GetCredentialsAsync();
        string loginServer = acr.Value.Data.LoginServer;
        string username = credentials.Value.Username;
        string password = credentials.Value.Passwords.First().Value;
        
        DeploymentLogger.Log($"Container Registry created: {loginServer}");
        
        return new ContainerRegistry(
            acr.Value.Id.ToString(),
            loginServer,
            username,
            password,
            resourceGroupName,
            azureCloud);
    }

    public async Task<bool> ContainsImage(string imageName)
    {
        try
        {
            DeploymentLogger.Log($"Checking if registry '{LoginServer}' contains image '{imageName}'...");

            await DockerLogin();
            string fullImageName = $"{LoginServer}/{imageName}:latest";
            return await DockerManifestExists(fullImageName);
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

        await DockerLogin();
        await DockerPull("hello-world");

        string targetImage = $"{LoginServer}/{imageName}:latest";
        await DockerTag("hello-world", targetImage);
        await DockerPush(targetImage);

        DeploymentLogger.Log($"Image '{imageName}' pushed to registry");
    }

    private static async Task DockerPull(string image) =>
        await RunDocker($"pull {image}");

    private static async Task DockerTag(string sourceImage, string targetImage) =>
        await RunDocker($"tag {sourceImage} {targetImage}");

    private static async Task DockerPush(string image) =>
        await RunDocker($"push {image}");

    private static async Task RunDocker(string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Docker command 'docker {arguments}' failed: {error}");
        }
    }

    private async Task DockerLogin()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"login {LoginServer} -u {Username} -p {Password}",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Docker login failed for registry check: {error}");
        }
    }

    private static async Task<bool> DockerManifestExists(string fullImageName)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"manifest inspect {fullImageName}",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            return true;
        }

        // "no such manifest" means the image is absent. Any other error is surfaced for troubleshooting.
        if (!error.Contains("no such manifest", StringComparison.OrdinalIgnoreCase))
        {
            DeploymentLogger.LogError($"Docker manifest inspect failed: {error}");
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await azureCloud.DeleteResourceGroup(resourceGroupName);
    }
}
