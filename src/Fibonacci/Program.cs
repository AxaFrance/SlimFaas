using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
IServiceCollection serviceCollection = builder.Services;
serviceCollection.AddSingleton<Fibonacci, Fibonacci>();
serviceCollection.AddCors();
serviceCollection.AddHttpClient();

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
    [FromServices] HttpClient client,
    FibonacciInput input) =>
{
    try
    {
        logger.LogInformation("Fibonacci Recursive Internal Called: {Input}", input.Input);
        var output = new FibonacciRecursiveOutput();
        if (input.Input <= 2)
        {
            output.Result = 1;
            output.NumberCall = 1;
            return output;
        }

        var response1 =
            client.PostAsJsonAsync(
                "http://slimfaas.slimfaas-demo.svc.cluster.local:5000/function/fibonacci1/fibonacci-recursive",
                new FibonacciInput() { Input = input.Input - 1 }, FibonacciInputSerializerContext.Default.FibonacciInput);
        var response2 =
            client.PostAsJsonAsync(
                "http://slimfaas.slimfaas-demo.svc.cluster.local:5000/function/fibonacci1/fibonacci-recursive",
                new FibonacciInput() { Input = input.Input - 2 }, FibonacciInputSerializerContext.Default.FibonacciInput);
        var result1 = JsonSerializer.Deserialize(await response1.Result.Content.ReadAsStringAsync(),
                FibonacciRecursiveOutputSerializerContext.Default.FibonacciRecursiveOutput);
        logger.LogInformation("Current result1: {Result}", result1.Result);
        var result2 = JsonSerializer.Deserialize(await response2.Result.Content.ReadAsStringAsync(),
            FibonacciRecursiveOutputSerializerContext.Default.FibonacciRecursiveOutput);

        logger.LogInformation("Current result2: {Result}", result2.Result);
        output.Result = result1.Result + result2.Result;
        output.NumberCall = result1.NumberCall + result2.NumberCall + 1;
        logger.LogInformation("Current output: {Result}", output.Result);
        return output;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error in Fibonacci Recursive Internal");
    }

    return new FibonacciRecursiveOutput();
});


app.MapPost("/send-private-fibonacci-event", (
    [FromServices] ILogger<Fibonacci> logger,
    [FromServices] Fibonacci fibonacci,
    [FromServices] HttpClient client,
    FibonacciInput input) =>
{
    logger.LogInformation("Fibonacci Private Event Called");
    var response = client.PostAsJsonAsync("http://slimfaas.slimfaas-demo.svc.cluster.local:5000/publish-event/fibo-private/fibonacci", input, FibonacciInputSerializerContext.Default.FibonacciInput).Result;
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

[JsonSerializable(typeof(FibonacciInput))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class FibonacciInputSerializerContext : JsonSerializerContext
{
}


public record FibonacciOutput {
    public int Result { get; set; }
}

public record FibonacciRecursiveOutput {
    public int Result { get; set; }
    public int NumberCall { get; set; }
}

[JsonSerializable(typeof(FibonacciRecursiveOutput))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class FibonacciRecursiveOutputSerializerContext : JsonSerializerContext
{
}

