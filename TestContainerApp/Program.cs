var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "TestContainerApp deployment successful!");
app.MapGet("/variable/{name}", (string name) =>
{
    string? value = Environment.GetEnvironmentVariable(name);
    return value == null ? Results.NotFound($"Environment variable '{name}' not found") : Results.Ok(value);
});

await app.RunAsync();
