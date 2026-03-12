using AzureIntegration;

namespace AzureTests;

public class ProjectPathResolverTests
{
    [TestCase("TestContainerApp", null, "TestContainerApp", "TestContainerApp")]
    [TestCase("App", "TestContainerAppWithProjectDependencies", "TestContainerAppWithProjectDependencies", "App")]
    public void GetBuildContextPaths_ReturnsExpectedPaths(
        string projectDirectory,
        string? workspaceRoot,
        string expectedBuildContext,
        string expectedProjectDir)
    {
        var (buildContext, projectDir) = ProjectPathResolver.GetBuildContextPaths(projectDirectory, workspaceRoot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buildContext.Name, Is.EqualTo(expectedBuildContext));
            Assert.That(projectDir.Name, Is.EqualTo(expectedProjectDir));
        }
    }

    [Test]
    public async Task GetBuildContextPaths_WhenRunFromSpecificDirectory_ReturnsExpectedPaths()
    {
        await Utils.RunTestFromDirectory(
            "TestContainerAppWithProjectDependencies/CallerProject/bin/Debug/net8.0",
            () =>
            {
                var (buildContext, projectDir) = ProjectPathResolver.GetBuildContextPaths("App", ".");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(buildContext.Name, Is.EqualTo("TestContainerAppWithProjectDependencies"));
                    Assert.That(projectDir.Name, Is.EqualTo("App"));
                }
                return Task.CompletedTask;
            });
    }

    [TestCase("NonExistentProject", null)]
    [TestCase("App", "NonExistentRoot")]
    public void GetBuildContextPaths_WhenDirectoryDoesNotExist_ThrowsDirectoryNotFoundException(
        string projectDirectory,
        string? workspaceRoot)
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            ProjectPathResolver.GetBuildContextPaths(projectDirectory, workspaceRoot));
    }

    [TestCase("Library", null)]
    [TestCase("Library", "TestContainerAppWithProjectDependencies")]
    public void GetBuildContextPaths_WhenProjectDirectoryHasNoDockerfile_ThrowsFileNotFoundException(
        string projectDirectory,
        string? workspaceRoot)
    {
        Assert.Throws<FileNotFoundException>(() =>
            ProjectPathResolver.GetBuildContextPaths(projectDirectory, workspaceRoot));
    }

    [Test]
    public async Task GetBuildContextPaths_WhenRunFromSpecificDirectoryAndProjectDirectoryHasNoDockerfile_ThrowsFileNotFoundException()
    {
        await Utils.RunTestFromDirectory(
            "TestContainerAppWithProjectDependencies/CallerProject/bin/Debug/net8.0",
            () =>
            {
                Assert.Throws<FileNotFoundException>(() =>
                    ProjectPathResolver.GetBuildContextPaths("Library", "."));
                return Task.CompletedTask;
            });
    }
}
