using System.Net.Http.Headers;
using Azure.Core;

namespace AzureIntegration;

internal static class ZipDeployer
{
    public static async Task Deploy(TokenCredential credentials, string zipFilePath, string serviceName)
    {
        using var httpClient = new HttpClient
                               {
                                   Timeout = TimeSpan.FromMinutes(10)
                               };
        string url = $"https://{serviceName.ToLower()}.scm.azurewebsites.net/api/zipdeploy";
        await using var fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var accessToken = await credentials.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), CancellationToken.None);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);

        Console.WriteLine($"Deploying zip file to {serviceName}...");
        var response = await httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Deployment failed for {serviceName}: {response.StatusCode} {errorContent}");
        }

        Console.WriteLine($"Zip file deployed successfully to {serviceName}");
    }
}
