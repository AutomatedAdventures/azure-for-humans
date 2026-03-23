using Azure.Core;
using AzureIntegration;
using System.Net;

namespace AzureTests;

public class ContainerAppTests
{
    private static string GenerateContainerAppName() =>
        $"testcontainerapp-{Guid.NewGuid().ToString("N")[..8]}";

    [Test]
    public async Task DeployContainerApp_WhenDeploymentFails_CleansUpResources()
    {
        var azure = new AzureCloud();
        string containerAppName = GenerateContainerAppName();

        Assert.ThrowsAsync<Exception>(async () =>
            await azure.DeployContainerApp(
                projectDirectory: "TestContainerAppWithBrokenDockerfile",
                name: containerAppName));
        
        bool resourceGroupExists = await azure.ResourceGroupExists(containerAppName);
        Assert.That(resourceGroupExists, Is.False, 
            $"Resource group '{containerAppName}' should have been cleaned up after deployment failure");
    }

    [Test, Category("LongRunning")]
    public async Task DeployContainerApp_WhenCallerProjectIsAtSolutionRoot_DeploysSuccessfully()
    {
        var azure = new AzureCloud(location: AzureLocation.EastUS);
        string containerAppName = GenerateContainerAppName();
        string workingDirectory = Path.Combine("TestContainerAppWithProjectDependencies", "CallerProject", "bin", "Debug", "net8.0");

        await Utils.RunTestFromDirectory(workingDirectory, async () =>
        {
            await using var containerApp = await azure.DeployContainerApp(
                projectDirectory: "App",
                name: containerAppName,
                workspaceRoot: ".");

            await AssertResourceGroupExists(azure, containerAppName);
            using var client = new HttpClient { BaseAddress = new Uri(containerApp.Url) };
            var response = await client.GetAsync("/");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            string content = await response.Content.ReadAsStringAsync();
            Assert.That(content, Is.EqualTo("TestContainerAppWithProjectDependencies deployment successful!"));
        });
    }


    [Test, Category("LongRunning")]
    public async Task DeployContainerApp_WithProjectDependencies()
    {
        var azure = new AzureCloud(location: AzureLocation.EastUS);
        string containerAppName = GenerateContainerAppName();

        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "App",
            name: containerAppName,
            workspaceRoot: "TestContainerAppWithProjectDependencies");

        await AssertResourceGroupExists(azure, containerAppName);
        using var client = new HttpClient { BaseAddress = new Uri(containerApp.Url) };
        var response = await client.GetAsync("/");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("TestContainerAppWithProjectDependencies deployment successful!"));
    }

    [Test, Category("LongRunning")]
    public async Task DeployContainerApp_WithDockerBuildArguments()
    {
        var azure = new AzureCloud(location: AzureLocation.EastUS);
        string containerAppName = GenerateContainerAppName();
        var buildArgs = new Dictionary<string, string>
                        {
                            { "APP_GREETING", "hello-from-build-arg" }
                        };

        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerAppWithDockerBuildArgs",
            name: containerAppName,
            dockerBuildArguments: buildArgs);

        await AssertResourceGroupExists(azure, containerAppName);
        using var client = new HttpClient { BaseAddress = new Uri(containerApp.Url) };
        var response = await client.GetAsync("/variable/APP_GREETING");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string value = await response.Content.ReadAsStringAsync();
        Assert.That(value.Trim('"'), Is.EqualTo("hello-from-build-arg"));
    }

    [Test]
    public async Task DeployContainerApp()
    {
        var azure = new AzureCloud(location: AzureLocation.EastUS);
        string containerAppName = GenerateContainerAppName();
        var envVars = new Dictionary<string, string>
                      {
                          { "MY_ENV_VAR1", "value1" },
                          { "MY_ENV_VAR2", "value2" }
                      };
        
        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: containerAppName,
            environmentVariables: envVars);

        await AssertResourceGroupExists(azure, containerAppName);
        await AssertContainerAppRespondsWithExpectedContent(containerApp);
        await AssertEnvironmentVariablesAreAccessible(containerApp, envVars);
        await AssertLogsAppearInApplicationInsights(containerApp);
        await AssertLogsAppearInApplicationInsightsWithCustomKqlQuery(containerApp);
    }

    private static async Task AssertLogsAppearInApplicationInsights(AzureContainerApp containerApp)
    {
        var expectedLog = "TestContainerApp request received.";
        var timeout = TimeSpan.FromMinutes(5);

        var logs = await WaitForLogToAppear(containerApp, expectedLog, timeout);

        Assert.That(logs.FirstOrDefault(log => log.Contains(expectedLog)), Is.Not.Null,
            $"Expected log not found in Application Insights within {timeout.TotalMinutes} minutes");
    }

    private static async Task AssertLogsAppearInApplicationInsightsWithCustomKqlQuery(AzureContainerApp containerApp)
    {
        var expectedLog = "custom-kql:TestContainerApp request received.";
        var customKqlQuery = "AppTraces | where Message != '' | project strcat('custom-kql:', Message) | limit 100";
        var timeout = TimeSpan.FromMinutes(5);

        var logs = await WaitForLogToAppear(containerApp, expectedLog, timeout, kqlQuery: customKqlQuery);

        Assert.That(logs.FirstOrDefault(log => log.Contains(expectedLog)), Is.Not.Null,
            $"Expected log not found using custom KQL query within {timeout.TotalMinutes} minutes");
    }

    private static async Task<IEnumerable<string>> WaitForLogToAppear(
        AzureContainerApp containerApp,
        string expectedLog,
        TimeSpan timeout,
        string? kqlQuery = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var pollingInterval = TimeSpan.FromSeconds(10);

        Console.WriteLine($"Waiting for Application Insights logs to appear (timeout: {timeout.TotalMinutes} min)...");

        IEnumerable<string> logs = [];

        while (stopwatch.Elapsed < timeout)
        {
            logs = kqlQuery is null
                ? containerApp.GetLogsFromApplicationInsights()
                : containerApp.GetLogsFromApplicationInsights(kqlQuery);

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

    private static async Task AssertResourceGroupExists(AzureCloud azure, string containerAppName)
    {
        bool resourceGroupExists = await azure.ResourceGroupExists(containerAppName);
        Assert.That(resourceGroupExists, Is.True, 
            $"Resource group '{containerAppName}' should exist after successful deployment");
    }

    private static async Task AssertContainerAppRespondsWithExpectedContent(AzureContainerApp containerApp)
    {
        using var client = new HttpClient { BaseAddress = new Uri(containerApp.Url) };
        var response = await client.GetAsync("/");
        
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("TestContainerApp deployment successful!"));
    }

    [Test, Category("LongRunning")]
    public async Task DeployContainerApp_WithManagedIdentity_CanReadKeyVaultSecretViaIdentity()
    {
        var azure = new AzureCloud(location: AzureLocation.EastUS);
        string containerAppName = GenerateContainerAppName();
        string secretValue = Guid.NewGuid().ToString();

        await using var identity = await azure.CreateUserAssignedIdentity(
            resourceGroupName: $"{containerAppName}-id",
            identityName: $"{containerAppName}-id");

        await using var keyVault = await azure.CreateKeyVaultWithSecret(
            resourceGroupName: $"{containerAppName}-kv",
            secretName: "test-secret",
            secretValue: secretValue,
            identityPrincipalId: identity.PrincipalId);

        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: containerAppName,
            managedIdentityResourceId: identity.ResourceId,
            environmentVariables: new Dictionary<string, string>
            {
                { "KEY_VAULT_URI", keyVault.Uri },
                { "KEY_VAULT_SECRET_NAME", "test-secret" }
            });

        using var client = new HttpClient { BaseAddress = new Uri(containerApp.Url) };
        var response = await client.GetAsync("/keyvault-secret");
        string retrievedSecret = await response.Content.ReadAsStringAsync();
        Assert.That(retrievedSecret.Trim('"'), Is.EqualTo(secretValue));
    }

    private static async Task AssertEnvironmentVariablesAreAccessible(
        AzureContainerApp containerApp, 
        Dictionary<string, string> envVars)
    {
        using var client = new HttpClient { BaseAddress = new Uri(containerApp.Url) };
        
        foreach (var (key, expectedValue) in envVars)
        {
            var response = await client.GetAsync($"/variable/{key}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Variable {key} not found");
            string value = await response.Content.ReadAsStringAsync();
            Assert.That(value.Trim('"'), Is.EqualTo(expectedValue), $"Variable {key} value mismatch");
        }
    }
}
