namespace AzureIntegration;

internal interface IDockerProcessRunner
{
    Task<DockerResult> RunAsync(string arguments, string? stdinInput = null, string? workingDirectory = null);
}

internal record DockerResult(int ExitCode, string StdOut, string StdErr);
