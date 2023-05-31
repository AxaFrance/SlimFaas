using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/fibonacci", (int input) => new Fibonacci().Run(input));

app.MapGet("/download", () =>
{
    using var processModule = Process.GetCurrentProcess().MainModule;
    var basePath = Path.GetDirectoryName(processModule?.FileName);
    var path = Path.Combine(basePath!, "dog.png");
    return Results.File(path, contentType:  "image/png");
});

app.MapGet("/hello/{name}", (string name) => $"Hello {name}!");

app.Run();

class Fibonacci
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