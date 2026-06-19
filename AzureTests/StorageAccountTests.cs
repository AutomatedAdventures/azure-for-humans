using AzureIntegration;

namespace AzureTests;

public class StorageAccountTests
{
    [TestCaseSource(typeof(TestExecutionContext), nameof(TestExecutionContext.All))]
    public async Task ACreatedStorageAccount_ExposesAConnectionStringThatNamesTheAccount(TestExecutionContext context)
    {
        await using var _ = context.Started();
        var azure = context.Azure();
        string name = $"storage{Guid.NewGuid().ToString("N")[..8]}";

        var resourceGroup = await azure.CreateResourceGroup(name);
        try
        {
            var storageAccount = await resourceGroup.CreateStorageAccount(name);

            Assert.That(storageAccount.ConnectionString, Does.Contain(storageAccount.Name),
                "A storage account's connection string should reference the account it connects to.");
        }
        finally
        {
            await azure.DeleteResourceGroup(name);
        }
    }
}
