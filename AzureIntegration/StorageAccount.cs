namespace AzureIntegration;

public class StorageAccount(string name, string connectionString)
{
    public string Name => name;
    public string ConnectionString => connectionString;
}
