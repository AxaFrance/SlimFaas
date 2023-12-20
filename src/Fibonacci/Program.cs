using Microsoft.AspNetCore.Mvc;

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
    int input) =>
{
    logger.LogDebug("Fibonacci Called");
    return fibonacci.Run(input);
});

app.MapGet("/download", ([FromServices] ILogger<Fibonacci> logger) =>
{
    logger.LogDebug("Download Called");
    string path = Path.Combine(Directory.GetCurrentDirectory(), "dog.png");
    return Results.File(path, "image/png");
});

app.MapGet("/hello/{name}", ([FromServices] ILogger<Fibonacci> logger, string name) =>
{
    logger.LogDebug("Hello Called");
    return $"Hello {name}!";
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
