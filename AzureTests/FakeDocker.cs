using AzureIntegration;

namespace AzureTests;

public class FakeDocker(FakeArmClient arm) : IDocker
{
    public Task Login(string server, string username, string password) => Task.CompletedTask;

    public Task Pull(string image) => Task.CompletedTask;

    public Task Tag(string sourceImage, string targetImage) => Task.CompletedTask;

    public Task Verify() => Task.CompletedTask;

    public Task Build(DirectoryInfo buildContext, string imageTag, string dockerfilePath, Dictionary<string, string>? buildArguments)
    {
        string fullDockerfilePath = Path.Combine(buildContext.FullName, dockerfilePath);
        string baseImage = ReadBaseImage(fullDockerfilePath);
        if (!baseImage.StartsWith("mcr.microsoft.com/", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Docker build failed: base image '{baseImage}' could not be pulled.");
        }

        string projectDirectory = Path.GetFileName(Path.GetDirectoryName(fullDockerfilePath))!;
        arm.RecordImageProject(imageTag, projectDirectory);
        arm.RecordImageBuildArguments(imageTag, buildArguments ?? new());
        return Task.CompletedTask;
    }

    private static string ReadBaseImage(string dockerfilePath)
    {
        string fromLine = File.ReadLines(dockerfilePath)
            .First(line => line.TrimStart().StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));
        return fromLine.Trim()["FROM ".Length..].Trim().Split(' ')[0];
    }

    public Task Push(string image)
    {
        var (loginServer, repository) = Split(image);
        arm.RegistryAt(loginServer)?.Images.Add(repository);
        return Task.CompletedTask;
    }

    public Task<bool> ManifestExists(string image)
    {
        var (loginServer, repository) = Split(image);
        var registry = arm.RegistryAt(loginServer);
        return Task.FromResult(registry?.Images.Contains(repository) ?? false);
    }

    private static (string loginServer, string repository) Split(string image)
    {
        int separator = image.IndexOf('/');
        return (image[..separator], image[(separator + 1)..]);
    }
}
