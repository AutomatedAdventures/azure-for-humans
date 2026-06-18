using AzureIntegration;

namespace AzureTests;

public class FakeDocker(FakeArmClient arm) : IDocker
{
    public Task Login(string server, string username, string password) => Task.CompletedTask;

    public Task Pull(string image) => Task.CompletedTask;

    public Task Tag(string sourceImage, string targetImage) => Task.CompletedTask;

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
