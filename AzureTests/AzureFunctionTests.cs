using System.Net;
using AzureIntegration;

namespace AzureTests;

public class AzureFunctionTests
{
    private static string GenerateFunctionName() =>
        $"testfunction-{Guid.NewGuid().ToString("N")[..8]}";

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployAzureFunction_WhenDeploymentFails_CleansUpResources(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string functionName = GenerateFunctionName();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await azure.DeployAzureFunction(
                projectDirectory: "TestAzureFunctionWithBuildError",
                name: functionName));
        
        Assert.That(exception!.Message, Does.Contain("Project build failed"));
        Assert.That(exception.Message, Does.Contain("TestAzureFunctionWithBuildError"));
        
        bool resourceGroupExists = await azure.ResourceGroupExists(functionName);
        Assert.That(resourceGroupExists, Is.False, 
            $"Resource group '{functionName}' should have been cleaned up after deployment failure");
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployAzureFunction(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string functionName = GenerateFunctionName();

        await using var function = await azure.DeployAzureFunction(
            projectDirectory: "AzureFunctionSample", 
            name: functionName);

        await AssertFunctionIsRunning(context, function);
        await AssertLogsAppearInApplicationInsights(function);
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployAzureFunction_WithEnvironmentVariables(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string functionName = GenerateFunctionName();
        var envVars = new Dictionary<string, string>
        {
            { "MY_ENV_VAR1", "value1" },
            { "MY_ENV_VAR2", "value2" }
        };
        
        await using var function = await azure.DeployAzureFunction(
            projectDirectory: "TestAzureFunction",
            name: functionName,
            environmentVariables: envVars);
        
        await AssertEnvironmentVariablesAreSet(context, function, envVars);
    }

    private static async Task AssertFunctionIsRunning(TestExecutionContext context, AzureFunction function)
    {
        using var client = context.HttpClientFor(function.Url);
        
        var response = await client.GetAsync("api/HttpTrigger");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("Azure Functions Sample test result"));
    }

    private static async Task AssertLogsAppearInApplicationInsights(AzureFunction function)
    {
        var expectedLog = "C# HTTP trigger function processed a request.";
        var timeout = TimeSpan.FromMinutes(5);
        
        var logs = await WaitForLogToAppear(function, expectedLog, timeout);
        
        Assert.That(logs.FirstOrDefault(log => log == expectedLog), Is.Not.Null, 
            $"Expected log not found in Application Insights within {timeout.TotalMinutes} minutes");
    }

    private static async Task<IEnumerable<string>> WaitForLogToAppear(
        AzureFunction function, 
        string expectedLog, 
        TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var pollingInterval = TimeSpan.FromSeconds(10);
        
        Console.WriteLine($"Waiting for Application Insights logs to appear (timeout: {timeout.TotalMinutes} min)...");
        
        IEnumerable<string> logs = [];
        
        while (stopwatch.Elapsed < timeout)
        {
            logs = function.GetLogsFromApplicationInsights();
            
            if (logs.Any(log => log.Contains(expectedLog)))
            {
                Console.WriteLine($"Application Insights logs found after {stopwatch.Elapsed.TotalSeconds:F1} seconds");
                return logs;
            }

            if (stopwatch.Elapsed < timeout)
            {
                Console.WriteLine($"Logs not ready yet. Waiting {pollingInterval.TotalSeconds} more seconds... (elapsed: {stopwatch.Elapsed.TotalSeconds:F1}s)");
                await Task.Delay(pollingInterval);
            }
        }
        
        return logs;
    }

    private static async Task AssertEnvironmentVariablesAreSet(
        TestExecutionContext context,
        AzureFunction function, 
        Dictionary<string, string> expectedVariables)
    {
        using var client = context.HttpClientFor(function.Url);
        
        foreach (var (name, expectedValue) in expectedVariables)
        {
            var response = await client.GetAsync($"api/variable/{name}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), 
                $"Variable {name} not found");
            
            string actualValue = await response.Content.ReadAsStringAsync();
            Assert.That(actualValue.Trim('"'), Is.EqualTo(expectedValue), 
                $"Variable {name} value mismatch");
        }
    }
}
