using Azure;
using Azure.Core;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;

namespace AzureIntegration;

public interface IApplicationLogs
{
    IEnumerable<string> GetLogs();
    IEnumerable<string> GetLogs(string kqlQuery);
}

public record FunctionDeployment(string Host, IApplicationLogs Logs);

public interface IFunctionService
{
    Task<FunctionDeployment> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string projectDirectory,
        Dictionary<string, string>? environmentVariables,
        string zipFilePath);
}

public class RealFunctionService(TokenCredential credentials) : IFunctionService
{
    public async Task<FunctionDeployment> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string projectDirectory,
        Dictionary<string, string>? environmentVariables,
        string zipFilePath)
    {
        var storageAccount = await resourceGroup.CreateStorageAccount(name);
        var appServicePlan = await resourceGroup.CreateAppServicePlanForFunctionApp(name);
        var applicationInsights = await resourceGroup.CreateApplicationInsights(name);

        var appSettings = new List<AppServiceNameValuePair>
                          {
                              new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                              new() { Name = "AzureWebJobsStorage", Value = storageAccount.ConnectionString },
                              new() { Name = "WEBSITE_RUN_FROM_PACKAGE", Value = "1" },
                              new() { Name = "FUNCTIONS_EXTENSION_VERSION", Value = "~4" },
                              new() { Name = "FUNCTIONS_WORKER_RUNTIME", Value = "dotnet-isolated" },
                              new() { Name = "SCM_DO_BUILD_DURING_DEPLOYMENT", Value = "0" },
                              new() { Name = "WEBSITE_USE_PLACEHOLDER_DOTNETISOLATED", Value = "1" },
                              new() { Name = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING", Value = storageAccount.ConnectionString },
                              new() { Name = "WEBSITE_CONTENTSHARE", Value = name.ToLower() },
                              new() { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = applicationInsights.ConnectionString },
                              new() { Name = "ApplicationInsightsAgent_EXTENSION_VERSION", Value = "~3" }
                          };
        appSettings = AzureCloud.AddEnvironmentVariablesToAppSettings(appSettings, environmentVariables);

        var functionAppData = new WebSiteData(resourceGroup.Resource.Data.Location)
                              {
                                  AppServicePlanId = appServicePlan.Id,
                                  Kind = "functionapp,linux",
                                  SiteConfig = new SiteConfigProperties
                                               {
                                                   AppSettings = appSettings,
                                                   LinuxFxVersion = "DOTNET-ISOLATED|8.0",
                                               }
                              };

        var functionApp = await resourceGroup.Resource.GetWebSites().CreateOrUpdateAsync(
                              WaitUntil.Completed, name, functionAppData);

        await AzureCloud.DeployZipFile(credentials, zipFilePath, name);

        Console.WriteLine($"Function App '{functionApp.Value.Data.Name}' created successfully.");

        return new FunctionDeployment(functionApp.Value.Data.DefaultHostName, applicationInsights);
    }
}

public class FailingFunctionService : IFunctionService
{
    public Task<FunctionDeployment> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string projectDirectory,
        Dictionary<string, string>? environmentVariables,
        string zipFilePath) =>
        throw new InvalidOperationException("This AzureCloud was not configured with a function service.");
}
