using Azure.Identity;
using Azure.Monitor.Query;
using Azure.ResourceManager.ApplicationInsights;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.OperationalInsights;

namespace AzureIntegration;

public class AzureFunction(
    WebSiteResource functionApp, 
    ApplicationInsightsComponentResource appInsights, 
    string resourceGroupName, 
    AzureCloud azureCloud)
    : IAsyncDisposable
{
    public string Name => functionApp.Data.Name;
    public string Url => functionApp.Data.DefaultHostName;

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    public IEnumerable<string> GetLogsFromApplicationInsights()
    {
        var credential = new DefaultAzureCredential();
        
        var armClient = new Azure.ResourceManager.ArmClient(credential);
        var workspaceResource = armClient.GetOperationalInsightsWorkspaceResource(appInsights.Data.WorkspaceResourceId!);
        var workspace = workspaceResource.Get();
        
        var workspaceId = workspace.Value.Data.CustomerId!;
        
        var client = new LogsQueryClient(credential);
        
        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime.AddHours(-1);
        
        var query = "AppTraces | where Message != '' | project Message | limit 100";
        
        var response = client.QueryWorkspace(workspaceId.ToString(), query, new QueryTimeRange(startTime, endTime));
        
        var logs = new List<string>();
        
        var table = response.Value.Table;
        
        foreach (var row in table.Rows)
        {
            if (row.Count > 0 && row[0] != null)
            {
                logs.Add(row[0].ToString() ?? string.Empty);
            }
        }
        
        return logs;
    }

    private async ValueTask DisposeAsync(bool deleteResourceGroup)
    {
        if (deleteResourceGroup)
        {
            await azureCloud.DeleteResourceGroup(resourceGroupName);
        }
    }
}
