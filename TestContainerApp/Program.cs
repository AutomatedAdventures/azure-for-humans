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

await app.RunAsync();
