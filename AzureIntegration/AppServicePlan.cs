using Azure.Core;

namespace AzureIntegration;

public class AppServicePlan(string name, ResourceIdentifier id)
{
    public string Name => name;
    public ResourceIdentifier Id => id;
}
