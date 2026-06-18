using System.Diagnostics;

namespace AzureIntegration;

public interface IDocker
{
    Task Login(string server, string username, string password);
    Task Pull(string image);
    Task Tag(string sourceImage, string targetImage);
    Task Push(string image);
    Task<bool> ManifestExists(string image);
}

internal class ProcessDocker : IDocker
{
    public Task Login(string server, string username, string password) =>
        Run($"login {server} -u {username} -p {password}");

    public Task Pull(string image) => Run($"pull {image}");

    public Task Tag(string sourceImage, string targetImage) => Run($"tag {sourceImage} {targetImage}");

    public Task Push(string image) => Run($"push {image}");

    public async Task<bool> ManifestExists(string image)
    {
        var (exitCode, error) = await Execute($"manifest inspect {image}");
        if (exitCode == 0)
        {
            return true;
        }

        if (!error.Contains("no such manifest", StringComparison.OrdinalIgnoreCase))
        {
            DeploymentLogger.LogError($"Docker manifest inspect failed: {error}");
        }

        return false;
    }

    private static async Task Run(string arguments)
    {
        var (exitCode, error) = await Execute(arguments);
        if (exitCode != 0)
        {
            throw new Exception($"Docker command 'docker {arguments}' failed: {error}");
        }
    }

    private static async Task<(int exitCode, string error)> Execute(string arguments)
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

        return (process.ExitCode, error);
    }
}
