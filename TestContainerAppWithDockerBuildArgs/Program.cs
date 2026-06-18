var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "TestContainerAppWithDockerBuildArgs deployment successful!");
app.MapGet("/variable/{name}", (string name) =>
{
    string? value = app.Configuration[name];
    return value == null ? Results.NotFound($"Environment variable '{name}' not found") : Results.Ok(value);
});

await app.RunAsync();

namespace DockerBuildArgsApp
{
    public class AppMarker;
}
