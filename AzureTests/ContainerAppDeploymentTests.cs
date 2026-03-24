using AzureIntegration;

namespace AzureTests;

public class ContainerAppDeploymentTests
{
    [Test]
    public async Task Deploy_DoesNotExposePasswordInDockerCommandLineArguments()
    {
        var docker = new DockerProcessSpy();
        var azure = new AzureCloud(
            infrastructure: new InMemoryInfrastructure(),
            dockerProcessRunner: docker);

        await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: "my-app");

        var loginCall = docker.Calls.First(c => c.Arguments.Contains("login"));
        Assert.That(loginCall.Arguments, Does.Not.Contain(InMemoryInfrastructure.AcrPassword),
            "Password must not appear in CLI arguments to prevent exposure in process list or logs");
    }

    [Test]
    public async Task Deploy_SendsPasswordViaStandardInput()
    {
        var docker = new DockerProcessSpy();
        var azure = new AzureCloud(
            infrastructure: new InMemoryInfrastructure(),
            dockerProcessRunner: docker);

        await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: "my-app");

        var loginCall = docker.Calls.First(c => c.Arguments.Contains("login"));
        Assert.That(loginCall.StdinInput, Is.EqualTo(InMemoryInfrastructure.AcrPassword),
            "Password should be sent via stdin using --password-stdin");
    }
}

internal record DockerProcessCall(string Arguments, string? StdinInput, string? WorkingDirectory);

internal class DockerProcessSpy : IDockerProcessRunner
{
    public List<DockerProcessCall> Calls { get; } = new();

    public Task<DockerResult> RunAsync(string arguments, string? stdinInput = null, string? workingDirectory = null)
    {
        Calls.Add(new DockerProcessCall(arguments, stdinInput, workingDirectory));
        return Task.FromResult(new DockerResult(0, "", ""));
    }
}
