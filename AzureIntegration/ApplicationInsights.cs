using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace AzureIntegration;

public record ApplicationInsights
{
    private readonly string _workspaceId;
    private readonly LogsQueryClient _logsQueryClient;

    public ApplicationInsights(string workspaceId, LogsQueryClient logsQueryClient, string connectionString)
    {
        _workspaceId = workspaceId;
        _logsQueryClient = logsQueryClient;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public IEnumerable<string> GetLogs()
    {
        return QueryLogs("AppTraces | where Message != '' | project Message | limit 100");
    }

    private IEnumerable<string> QueryLogs(string query)
    {
        var response = ExecuteQuery(query);
        return FormatResponse(response);
    }

    private LogsQueryResult ExecuteQuery(string query)
    {
        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime.AddHours(-1);
        
        var response = _logsQueryClient.QueryWorkspace(_workspaceId, query, new QueryTimeRange(startTime, endTime));
        
        return response.Value;
    }

    private static IEnumerable<string> FormatResponse(LogsQueryResult queryResult)
    {
        var logs = new List<string>();
        
        var table = queryResult.Table;
        
        foreach (var row in table.Rows)
        {
            if (row.Count > 0 && row[0] != null)
            {
                logs.Add(row[0].ToString() ?? string.Empty);
            }
        }
        
        return logs;
    }
}
