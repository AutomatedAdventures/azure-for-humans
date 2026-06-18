using Azure;
using Azure.Core;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;

namespace AzureIntegration;

public interface IAppService
{
    Task<string> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string projectDirectory,
        Dictionary<string, string>? environmentVariables,
        string zipFilePath);
}

public class RealAppService(TokenCredential credentials) : IAppService
{
    public async Task<string> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string projectDirectory,
        Dictionary<string, string>? environmentVariables,
        string zipFilePath)
    {
        string appServicePlanName = $"{name.ToLower()}-plan-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var appServicePlan = await resourceGroup.CreateAppServicePlanForWebApp(appServicePlanName);

        var appSettings = new List<AppServiceNameValuePair>
                          {
                              new() { Name = "DEPLOYMENT_DATE", Value = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
                          };
        appSettings = AzureCloud.AddEnvironmentVariablesToAppSettings(appSettings, environmentVariables);

        var webAppData = new WebSiteData(resourceGroup.Resource.Data.Location)
                         {
                             AppServicePlanId = appServicePlan.Id,
                             Kind = "app,linux",
                             SiteConfig = new SiteConfigProperties
                                          {
                                              AppSettings = appSettings,
                                              LinuxFxVersion = "DOTNETCORE|8.0"
                                          }
                         };
        var webApp = await resourceGroup.Resource.GetWebSites().CreateOrUpdateAsync(
                         WaitUntil.Completed, name, webAppData);

        await AzureCloud.DeployZipFile(credentials, zipFilePath, name);

        await AzureCloud.WaitForAppServiceToBeReady(webApp.Value.Data.DefaultHostName);

        return webApp.Value.Data.DefaultHostName;
    }
}

public class FailingAppService : IAppService
{
    public Task<string> Deploy(
        ResourceGroup resourceGroup,
        string name,
        string projectDirectory,
        Dictionary<string, string>? environmentVariables,
        string zipFilePath) =>
        throw new InvalidOperationException("This AzureCloud was not configured with an app service.");
}
