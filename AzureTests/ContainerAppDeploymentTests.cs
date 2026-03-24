using AzureIntegration;

namespace AzureTests;

public class ContainerAppDeploymentTests
{
    private readonly InMemoryInfrastructure _infrastructure = new();
    private readonly DockerProcessSpy _docker = new();

    [SetUp]
    public async Task DeployContainerApp()
    {
        var azure = new AzureCloud(
            infrastructure: _infrastructure,
            dockerProcessRunner: _docker);

        await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: "my-app");
    }

    [Test]
    public void RegistryPassword_IsNotExposedInCommandLineArguments()
    {
        Assert.That(_docker.LoginCall.Arguments, Does.Not.Contain(_infrastructure.RegistryPassword));
    }

    [Test]
    public void RegistryPassword_IsSentViaStandardInput()
    {
        Assert.That(_docker.LoginCall.StdinInput, Is.EqualTo(_infrastructure.RegistryPassword));
    }
}

internal record DockerProcessCall(string Arguments, string? StdinInput, string? WorkingDirectory);

internal class DockerProcessSpy : IDockerProcessRunner
{
    public List<DockerProcessCall> Calls { get; } = new();
    public DockerProcessCall LoginCall => Calls.First(c => c.Arguments.Contains("login"));

    public Task<DockerResult> RunAsync(string arguments, string? stdinInput = null, string? workingDirectory = null)
    {
        Calls.Add(new DockerProcessCall(arguments, stdinInput, workingDirectory));
        return Task.FromResult(new DockerResult(0, "", ""));
    }
}
