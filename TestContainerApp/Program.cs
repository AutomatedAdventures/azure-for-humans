using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationInsightsTelemetry();
var app = builder.Build();

app.MapGet("/", (ILogger<Program> logger) =>
{
    logger.LogInformation("TestContainerApp request received.");
    return "TestContainerApp deployment successful!";
});
app.MapGet("/variable/{name}", (string name) =>
{
    string? value = Environment.GetEnvironmentVariable(name);
    return value == null ? Results.NotFound($"Environment variable '{name}' not found") : Results.Ok(value);
});
app.MapGet("/keyvault-secret", async () =>
{
    string keyVaultUri = Environment.GetEnvironmentVariable("KEY_VAULT_URI")!;
    string secretName = Environment.GetEnvironmentVariable("KEY_VAULT_SECRET_NAME")!;
    var client = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
    KeyVaultSecret secret = await client.GetSecretAsync(secretName);
    return secret.Value;
});

await app.RunAsync();
