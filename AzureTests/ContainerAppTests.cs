using Azure.Core;
using AzureIntegration;
using System.Net;

namespace AzureTests;

public class ContainerAppTests
{
    private static string GenerateContainerAppName() =>
        $"testcontainerapp-{Guid.NewGuid().ToString("N")[..8]}";

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployContainerApp_WhenDeploymentFails_CleansUpResources(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string containerAppName = GenerateContainerAppName();

        Assert.ThrowsAsync<Exception>(async () =>
            await azure.DeployContainerApp(
                projectDirectory: "TestContainerAppWithBrokenDockerfile",
                name: containerAppName));
        
        bool resourceGroupExists = await azure.ResourceGroupExists(containerAppName);
        Assert.That(resourceGroupExists, Is.False, 
            $"Resource group '{containerAppName}' should have been cleaned up after deployment failure");
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployContainerApp_WhenCallerProjectIsAtSolutionRoot_DeploysSuccessfully(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string containerAppName = GenerateContainerAppName();
        string workingDirectory = Path.Combine("TestContainerAppWithProjectDependencies", "CallerProject", "bin", "Debug", "net8.0");

        await Utils.RunTestFromDirectory(workingDirectory, async () =>
        {
            await using var containerApp = await azure.DeployContainerApp(
                projectDirectory: "App",
                name: containerAppName,
                workspaceRoot: ".");

            await AssertResourceGroupExists(azure, containerAppName);
            using var client = context.HttpClientFor(containerApp.Url);
            var response = await client.GetAsync("/");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            string content = await response.Content.ReadAsStringAsync();
            Assert.That(content, Is.EqualTo("TestContainerAppWithProjectDependencies deployment successful!"));
        });
    }


    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployContainerApp_WithProjectDependencies(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string containerAppName = GenerateContainerAppName();

        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "App",
            name: containerAppName,
            workspaceRoot: "TestContainerAppWithProjectDependencies");

        await AssertResourceGroupExists(azure, containerAppName);
        using var client = context.HttpClientFor(containerApp.Url);
        var response = await client.GetAsync("/");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("TestContainerAppWithProjectDependencies deployment successful!"));
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployContainerApp_WithDockerBuildArguments(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
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
        using var client = context.HttpClientFor(containerApp.Url);
        var response = await client.GetAsync("/variable/APP_GREETING");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string value = await response.Content.ReadAsStringAsync();
        Assert.That(value.Trim('"'), Is.EqualTo("hello-from-build-arg"));
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployContainerApp(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
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
        await AssertContainerAppRespondsWithExpectedContent(context, containerApp);
        await AssertEnvironmentVariablesAreAccessible(context, containerApp, envVars);
        await AssertLogsAppearInApplicationInsights(context.Time, containerApp);
        await AssertLogsAppearInApplicationInsightsWithCustomKqlQuery(context.Time, containerApp);
    }

    private static async Task AssertLogsAppearInApplicationInsights(TimeProvider time, AzureContainerApp containerApp)
    {
        var expectedLog = "TestContainerApp request received.";
        var timeout = TimeSpan.FromMinutes(5);

        var logs = await WaitForLogToAppear(time, containerApp, expectedLog, timeout);

        Assert.That(logs.FirstOrDefault(log => log.Contains(expectedLog)), Is.Not.Null,
            $"Expected log not found in Application Insights within {timeout.TotalMinutes} minutes");
    }

    private static async Task AssertLogsAppearInApplicationInsightsWithCustomKqlQuery(TimeProvider time, AzureContainerApp containerApp)
    {
        var expectedLog = "custom-kql:TestContainerApp request received.";
        var customKqlQuery = "AppTraces | where Message != '' | project strcat('custom-kql:', Message) | limit 100";
        var timeout = TimeSpan.FromMinutes(5);

        var logs = await WaitForLogToAppear(time, containerApp, expectedLog, timeout, kqlQuery: customKqlQuery);

        Assert.That(logs.FirstOrDefault(log => log.Contains(expectedLog)), Is.Not.Null,
            $"Expected log not found using custom KQL query within {timeout.TotalMinutes} minutes");
    }

    private static async Task<IEnumerable<string>> WaitForLogToAppear(
        TimeProvider time,
        AzureContainerApp containerApp,
        string expectedLog,
        TimeSpan timeout,
        string? kqlQuery = null)
    {
        var startedAt = time.GetUtcNow();
        var pollingInterval = TimeSpan.FromSeconds(10);

        Console.WriteLine($"Waiting for Application Insights logs to appear (timeout: {timeout.TotalMinutes} min)...");

        IEnumerable<string> logs = [];

        while (time.GetUtcNow() - startedAt < timeout)
        {
            logs = kqlQuery is null
                ? containerApp.GetLogsFromApplicationInsights()
                : containerApp.GetLogsFromApplicationInsights(kqlQuery);

            if (logs.Any(log => log.Contains(expectedLog)))
            {
                return logs;
            }

            await Task.Delay(pollingInterval, time);
        }

        return logs;
    }

    private static async Task AssertResourceGroupExists(AzureCloud azure, string containerAppName)
    {
        bool resourceGroupExists = await azure.ResourceGroupExists(containerAppName);
        Assert.That(resourceGroupExists, Is.True, 
            $"Resource group '{containerAppName}' should exist after successful deployment");
    }

    [Test]
    public async Task WhenTheFirstRegionHasNoCapacityForTheContainerAppEnvironment_TheDeploymentFailsOverToTheNextRegion()
    {
        await using var context = new FakeExecutionContext()
            .WithRegions(AzureLocation.EastUS, AzureLocation.WestEurope)
            .WithNoContainerAppCapacityIn(AzureLocation.EastUS);
        var azure = context.Azure();
        string containerAppName = GenerateContainerAppName();

        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: containerAppName);

        var resourceGroup = (await azure.GetResourceGroups()).Single(group => group.Name == containerAppName);
        Assert.That(resourceGroup.Location, Is.EqualTo(AzureLocation.WestEurope),
            "When the first region has no container app capacity, the deployment should fail over to the next configured region.");
    }
    private static async Task AssertContainerAppRespondsWithExpectedContent(TestExecutionContext context, AzureContainerApp containerApp)
    {
        using var client = context.HttpClientFor(containerApp.Url);
        var response = await client.GetAsync("/");
        
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        string content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("TestContainerApp deployment successful!"));
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployContainerApp_WithManagedIdentity_CanReadKeyVaultSecretViaIdentity(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
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
                { "KEY_VAULT_SECRET_NAME", "test-secret" },
                { "AZURE_CLIENT_ID", identity.ClientId.ToString() }
            });

        using var client = context.HttpClientFor(containerApp.Url);
        var response = await client.GetAsync("/keyvault-secret");
        string retrievedSecret = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"/keyvault-secret response ({(int)response.StatusCode}): {retrievedSecret}");
        Assert.That(retrievedSecret.Trim('"'), Is.EqualTo(secretValue),
            "A container app with a managed identity should read the Key Vault secret it was granted access to.");
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DeployContainerApp_UsesTheProvidedContainerRegistry(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string containerAppName = GenerateContainerAppName();

        await using var providedRegistry = await azure.CreateContainerRegistry(
            resourceGroupName: $"{containerAppName}-registry",
            registryName: $"existingregistry{Guid.NewGuid().ToString("N")[..8]}");

        await using var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: containerAppName,
            containerRegistryResourceId: providedRegistry.ResourceId);

        bool imageIsHostedInProvidedRegistry = await providedRegistry.ContainsImage(containerAppName.ToLower());
        Assert.That(imageIsHostedInProvidedRegistry, Is.True,
            "Expected the provided container registry to host the deployed image, but the deployment used a different registry.");
    }

    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task DisposingContainerApp_DoesNotRemoveTheProvidedContainerRegistry(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string containerAppName = GenerateContainerAppName();
        string registryResourceGroupName = $"{containerAppName}-registry";

        await using var providedRegistry = await azure.CreateContainerRegistry(
            resourceGroupName: registryResourceGroupName,
            registryName: $"existingregistry{Guid.NewGuid().ToString("N")[..8]}");

        var containerApp = await azure.DeployContainerApp(
            projectDirectory: "TestContainerApp",
            name: containerAppName,
            containerRegistryResourceId: providedRegistry.ResourceId);
        await containerApp.DisposeAsync();

        bool providedRegistryStillExists = await providedRegistry.Exists();
        Assert.That(providedRegistryStillExists, Is.True,
            "Disposing the container app should not remove the container registry that was provided by the caller.");
    }

    private static async Task AssertEnvironmentVariablesAreAccessible(
        TestExecutionContext context,
        AzureContainerApp containerApp, 
        Dictionary<string, string> envVars)
    {
        using var client = context.HttpClientFor(containerApp.Url);
        
        foreach (var (key, expectedValue) in envVars)
        {
            var response = await client.GetAsync($"/variable/{key}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Variable {key} not found");
            string value = await response.Content.ReadAsStringAsync();
            Assert.That(value.Trim('"'), Is.EqualTo(expectedValue), $"Variable {key} value mismatch");
        }
    }
}
