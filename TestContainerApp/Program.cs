using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddSingleton<ContainerApp.ISecretReader, ContainerApp.KeyVaultSecretReader>();
var app = builder.Build();

app.MapGet("/", (ILogger<Program> logger) =>
{
    logger.LogInformation("TestContainerApp request received.");
    return "TestContainerApp deployment successful!";
});
app.MapGet("/variable/{name}", (string name) =>
{
    string? value = app.Configuration[name];
    return value == null ? Results.NotFound($"Environment variable '{name}' not found") : Results.Ok(value);
});
app.MapGet("/keyvault-secret", async (ContainerApp.ISecretReader secretReader) =>
{
    string keyVaultUri = app.Configuration["KEY_VAULT_URI"]!;
    string secretName = app.Configuration["KEY_VAULT_SECRET_NAME"]!;
    try
    {
        return Results.Ok(await secretReader.Read(keyVaultUri, secretName));
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to read secret '{secretName}' from '{keyVaultUri}': {ex}");
    }
});

await app.RunAsync();

namespace ContainerApp
{
    public class AppMarker;

    public interface ISecretReader
    {
        Task<string> Read(string vaultUri, string secretName);
    }

    public class KeyVaultSecretReader : ISecretReader
    {
        public async Task<string> Read(string vaultUri, string secretName)
        {
            var client = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());
            KeyVaultSecret secret = await client.GetSecretAsync(secretName);
            return secret.Value;
        }
    }
}

