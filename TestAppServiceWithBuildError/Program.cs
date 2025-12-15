// Este código tiene errores de compilación intencionados
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => UndefinedMethod()); // Error: método no existe

app.Run();
