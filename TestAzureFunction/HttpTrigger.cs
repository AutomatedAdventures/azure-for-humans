using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TestAzureFunction
{
    public class HttpTrigger
    {
        private readonly ILogger<HttpTrigger> _logger;

        public HttpTrigger(ILogger<HttpTrigger> logger)
        {
            _logger = logger;
        }

        [Function("HttpTrigger")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult("Azure Functions Sample test result");
        }

        [Function("Variable")]
        public static IActionResult GetVariable(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "variable/{name}")] HttpRequest req,
            string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return value == null 
                       ? new NotFoundResult() 
                       : new OkObjectResult(value);
        }

        [Function("Variables")]
        public static IActionResult GetVariables(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "variables")] HttpRequest req)
        {
            var envVars = Environment.GetEnvironmentVariables();
            var dict = new Dictionary<string, string>();
            foreach (object? key in envVars.Keys)
            {
                dict[key.ToString()!] = envVars[key]?.ToString() ?? string.Empty;
            }
            return new OkObjectResult(dict);
        }
    }
}
