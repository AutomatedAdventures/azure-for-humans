var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "TestAppService deployment successful!");
app.MapGet("/variable/{name}", (string name) => Environment.GetEnvironmentVariable(name));

app.Run();
