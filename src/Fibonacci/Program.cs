using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
IServiceCollection serviceCollection = builder.Services;
serviceCollection.AddSingleton<Fibonacci, Fibonacci>();
serviceCollection.AddCors();

WebApplication app = builder.Build();
app.UseCors(builder => builder
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader()
);



app.MapPost("/fibonacci", (
    [FromServices] ILogger<Fibonacci> logger,
    [FromServices] Fibonacci fibonacci,
    FibonacciInput input) =>
{
    logger.LogInformation("Fibonacci Called");
    var output = new FibonacciOutput();
    output.Result = fibonacci.Run(input.Input);
    logger.LogInformation("Fibonacci output: {Output}", output.Result);
    return output;
});

app.MapGet("/download", ([FromServices] ILogger<Fibonacci> logger) =>
{
    logger.LogInformation("Download Called");
    string path = Path.Combine(Directory.GetCurrentDirectory(), "dog.png");
    return Results.File(path, "image/png");
});

app.MapGet("/hello/{name}", ([FromServices] ILogger<Fibonacci> logger, string name) =>
{
    logger.LogInformation("Hello Called with name: {Name}", name);
    return $"Hello {name}!";
});


app.MapGet("/health", () => "OK");


app.MapPost("/fibonacci-recursive", async (
    [FromServices] ILogger<Fibonacci> logger,
    [FromServices] Fibonacci fibonacci,
    FibonacciInput input) =>
{
    logger.LogInformation("Fibonacci Recursive Internal Called");
    using HttpClient client = new();
    var output = new FibonacciOutput();
    if (input.Input <= 2)
    {
        output.Result = 1;
        return output;
    }
    var response1 = client.PostAsJsonAsync("http://slimfaas.slimfaas-demo.svc.cluster.local:5000/function/fibonacci1/fibonacci-recursive", new FibonacciInput(){ Input = input.Input-1});
    var response2 = client.PostAsJsonAsync("http://slimfaas.slimfaas-demo.svc.cluster.local:5000/function/fibonacci1/fibonacci-recursive", new FibonacciInput(){ Input = input.Input-2});
    if (response1.Result.IsSuccessStatusCode || response2.Result.IsSuccessStatusCode)
    {
        throw new Exception("Request Fail");
    }
    var result1 = JsonConvert.DeserializeObject<FibonacciOutput>(await response1.Result.Content.ReadAsStringAsync());
    var result2 = JsonConvert.DeserializeObject<FibonacciOutput>(await response2.Result.Content.ReadAsStringAsync());
    output.Result = result1.Result + result2.Result;
    logger.LogInformation("Current output: {Result}", output.Result);
    return output;
});


app.MapPost("/send-private-fibonacci-event", (
    [FromServices] ILogger<Fibonacci> logger,
    [FromServices] Fibonacci fibonacci,
    FibonacciInput input) =>
{
    logger.LogInformation("Fibonacci Private Event Called");
    using HttpClient client = new();
    var response = client.PostAsJsonAsync("http://slimfaas.slimfaas-demo.svc.cluster.local:5000/publish-event/fibo-private/fibonacci", input).Result;
    logger.LogInformation("Response status code: {StatusCode}", response.StatusCode);
    logger.LogInformation("Fibonacci Internal Event End");
});





app.Run();

internal class Fibonacci
{
    public int Run(int i)
    {
        if (i <= 2)
        {
            return 1;
        }

        return Run(i - 1) + Run(i - 2);
    }
}

public record FibonacciInput {
    public int Input { get; set; }
}

public record FibonacciOutput {
    public int Result { get; set; }
}
