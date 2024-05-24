using Microsoft.AspNetCore.Mvc;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
IServiceCollection serviceCollection = builder.Services;
serviceCollection.AddSingleton<Fibonacci, Fibonacci>();
serviceCollection.AddCors();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


WebApplication app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
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
    logger.LogDebug("Fibonacci Called");
    var output = new FibonacciOutput();
    output.Result = fibonacci.Run(input.Input);
    return output;
});
/*
app.MapPost("/crazy-fibonacci", (
    [FromServices] ILogger<Fibonacci> logger,
    [FromServices] Fibonacci fibonacci,
    FibonacciInput input) =>
{
    logger.LogDebug("Fibonacci Called");
    var output = new FibonacciOutput();
    output.Result = fibonacci.Run(input.Input);
    return output;
});*/

// http://localhost:30021/function/fibonacci1/hello/guillaume

app.MapGet("/download", ([FromServices] ILogger<Fibonacci> logger) =>
{
    logger.LogDebug("Download Called");
    string path = Path.Combine(Directory.GetCurrentDirectory(), "dog.png");
    return Results.File(path, "image/png");
}).WithDescription("Some Method Description");

app.MapGet("/hello/{name}", ([FromServices] ILogger<Fibonacci> logger, string name) =>
{
    logger.LogDebug("Hello Called");
    return $"Hello {name}!";
}).WithDescription("Some Method Description");

app.MapGet("/health", () => "OK").WithDescription("Some Method Description");

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
/*
internal class CrazyFibonacci
{
    private readonly HttpClient _httpClient;

    public CrazyFibonacci(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<int> Run(int i)
    {
        if (i <= 2)
        {
            return 1;
        }

        var random =new Random();
        var value = random.Next(1, 3);
        var response = await _httpClient.PostAsJsonAsync($"http://localhost:30021/function/fibonacci{value}", new FibonacciInput(){Input = i});

        var content = await response.Content.ReadAsStringAsync();


        return Run(i - 1) + Run(i - 2);
    }
}*/

public record FibonacciInput{
    public int Input { get; set; }
}

public record FibonacciOutput{
    public int Result { get; set; }
}
