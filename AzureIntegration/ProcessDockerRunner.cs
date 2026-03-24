using System.Diagnostics;

namespace AzureIntegration;

internal class ProcessDockerRunner : IDockerProcessRunner
{
    public async Task<DockerResult> RunAsync(string arguments, string? stdinInput = null, string? workingDirectory = null)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdinInput != null,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (workingDirectory != null)
            process.StartInfo.WorkingDirectory = workingDirectory;

        process.Start();

        if (stdinInput != null)
        {
            await process.StandardInput.WriteAsync(stdinInput);
            process.StandardInput.Close();
        }

        string stdOut = await process.StandardOutput.ReadToEndAsync();
        string stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new DockerResult(process.ExitCode, stdOut, stdErr);
    }
}
