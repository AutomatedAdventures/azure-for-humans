using Azure.Identity;
using AzureIntegration;

namespace AzureTests;

public class AuthenticationTests
{
    [Test]
    public void CheckAzureEnvironmentVariablesAreDefined()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"), Is.Not.Null.And.Not.Empty);
            Assert.That(Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET"), Is.Not.Null.And.Not.Empty);
            Assert.That(Environment.GetEnvironmentVariable("AZURE_TENANT_ID"), Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void PassUserTokenCredentials()
    {
        var azure = new AzureCloud(new DefaultAzureCredential());
        var resourceGroups = azure.GetResourceGroups().Result;
        Assert.That(resourceGroups.Count, Is.GreaterThan(0));
    }

    [Test]
    public void PassInvalidCredentials()
    {
        var azure = new AzureCloud(new ClientSecretCredential("invalid-client-id", "invalid-client-secret", "invalid-tenant-id"));
        Assert.ThrowsAsync<AuthenticationFailedException>(() => azure.GetResourceGroups());
    }
}
