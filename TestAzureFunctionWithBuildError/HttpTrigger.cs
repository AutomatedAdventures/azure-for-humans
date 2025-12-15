// Error de compilación intencionado
using Microsoft.Azure.Functions.Worker;

public class HttpTrigger
{
    [Function("HttpTrigger")]
    public string Run()
    {
        return UndefinedMethod(); // Error: método no existe
    }
}
