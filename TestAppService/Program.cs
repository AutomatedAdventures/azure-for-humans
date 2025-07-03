var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "TestAppService deployment successful!");
app.MapGet("/variable/{name}", (string name) => 
{
    var value = Environment.GetEnvironmentVariable(name);
    if (value == null)
    {
        return Results.NotFound($"Environment variable '{name}' not found");
    }
    return Results.Ok(value);
});

app.Run();
