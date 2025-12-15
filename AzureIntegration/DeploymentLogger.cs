using System.Diagnostics;

namespace AzureIntegration;

internal static class DeploymentLogger
{
    private static readonly Stopwatch Stopwatch = new();
    private static TimeSpan _lastLogTime = TimeSpan.Zero;

    public static void Start(string message)
    {
        Stopwatch.Restart();
        _lastLogTime = TimeSpan.Zero;
        Log(message);
    }

    public static void Log(string message)
    {
        var elapsed = Stopwatch.Elapsed;
        var delta = elapsed - _lastLogTime;
        _lastLogTime = elapsed;

        string timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
        string elapsedStr = $"{elapsed:mm\\:ss}";
        string deltaStr = $"+{delta.TotalSeconds:F1}s";

        Console.WriteLine($"[{timestamp}] [{elapsedStr}] [{deltaStr}] {message}");
    }

    public static void LogError(string message)
    {
        Log($"ERROR: {message}");
    }
}
