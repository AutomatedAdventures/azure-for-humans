namespace AzureIntegration;

internal static class ProjectPathResolver
{
    internal static (DirectoryInfo buildContext, DirectoryInfo projectDir) GetBuildContextPaths(
        string projectDirectory,
        string? workspaceRoot)
    {
        DirectoryInfo buildContext, projectDir;
        if (workspaceRoot is null)
        {
            projectDir = ValidateProjectDirectory(GetProjectDirectory(projectDirectory));
            buildContext = projectDir;
        }
        else
        {
            buildContext = workspaceRoot == "." ? GetExecutionRoot() : GetProjectDirectory(workspaceRoot);
            projectDir = ValidateProjectDirectory(new DirectoryInfo(Path.Combine(buildContext.FullName, projectDirectory)));
        }

        return (buildContext, projectDir);
    }

    internal static DirectoryInfo GetExecutionRoot() 
        => new DirectoryInfo(Directory.GetCurrentDirectory()).Parent!.Parent!.Parent!.Parent!;

    private static DirectoryInfo GetProjectDirectory(string projectDirectory) 
        => GetExecutionRoot()
            .GetDirectories(projectDirectory, SearchOption.AllDirectories)
            .FirstOrDefault() ?? throw new DirectoryNotFoundException($"Project directory not found: {projectDirectory}");

    private static DirectoryInfo ValidateProjectDirectory(DirectoryInfo projectDirectory)
    {
        DeploymentLogger.Log($"Project directory: {projectDirectory.FullName}");
        if (!File.Exists(Path.Combine(projectDirectory.FullName, "Dockerfile")))
            throw new FileNotFoundException($"Dockerfile not found in: {projectDirectory.FullName}");
        return projectDirectory;
    }
}
