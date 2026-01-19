using Azure.ResourceManager.ApplicationInsights;

namespace AzureIntegration;

public record ApplicationInsightsComponent(ApplicationInsightsComponentResource Resource)
{
    public string ConnectionString => Resource.Data.ConnectionString ?? string.Empty;
}
