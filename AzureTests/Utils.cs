namespace AzureTests;

internal static class Utils
{
    internal static async Task RunTestFromDirectory(string directoryRelativeToRepoRoot, Func<Task> test)
    {
        string savedDir = Directory.GetCurrentDirectory();
        var repoRoot = new DirectoryInfo(savedDir).Parent!.Parent!.Parent!.Parent!.FullName;
        var directory = Path.Combine(repoRoot, directoryRelativeToRepoRoot);
        Directory.CreateDirectory(directory);
        try
        {
            Directory.SetCurrentDirectory(directory);
            await test();
        }
        finally
        {
            Directory.SetCurrentDirectory(savedDir);
            Directory.Delete(directory, recursive: true);
        }
    }
}
