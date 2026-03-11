using Library;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Greeter.GetGreeting());

await app.RunAsync();
