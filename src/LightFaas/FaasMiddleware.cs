using System.Text.Json;
using LightFaas;

public class FaasMiddleware 
{
    private readonly RequestDelegate _next;
    private readonly IServiceProvider _serviceProvider;
    private readonly IQueue _queue;

    public FaasMiddleware(RequestDelegate next,IServiceProvider serviceProvider, IQueue queue)
    {
        _next = next;
        _serviceProvider = serviceProvider;
        _queue = queue;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<FaasMiddleware> faasLogger)
    {
        IList<CustomHeader> customHeaders = new List<CustomHeader>();
        var contextRequest = context.Request;
        foreach (var headers in contextRequest.Headers)
        {
            var customHeader = new CustomHeader()
            {
                Key = headers.Key,
                Values = headers.Value.ToArray()
            };
            customHeaders.Add(customHeader);
        }
    
        var method = contextRequest.Method;
        var path = contextRequest.Path;
        var queryString = contextRequest.QueryString;
    
        IList<CustomForm> customForms = new List<CustomForm>();
        IList<CustomFormFile> customFormFiles = new List<CustomFormFile>();
        string body = null;
        string contentType = null;
        if (method == "POST")
        {
            var rawList = new List<string>() { "text/plain", "application/json" };
            var ct = rawList
                .FirstOrDefault(r => contextRequest.Headers.ContentType.ToString().ToLower().Contains(r));
            if( ct != null)
            {
                contentType = ct;
                using var reader = new StreamReader(contextRequest.Body);
                body = await reader.ReadToEndAsync();
            }
            else
            {
                contentType = "multipart/form-data";
                foreach (var formData in contextRequest.Form)
                {
                    var customForm = new CustomForm()
                    {
                        Key = formData.Key,
                        Values = formData.Value.ToArray()
                    };
                    customForms.Add(customForm);
                }

                if (contextRequest.Form.Files != null && contextRequest.Form.Files.Count > 0)
                {
                    foreach (var formFile in contextRequest.Form.Files)
                    {
                        using var memoryStream = new MemoryStream();
                        await formFile.CopyToAsync(memoryStream);
                        var customFormFile = new CustomFormFile()
                        {
                            Key = formFile.Name,
                            Value = memoryStream.ToArray(),
                            Filename = formFile.FileName
                        };
                        customFormFiles.Add(customFormFile);
                    }
                }
            }
        }

        var functionBeginPath = String.Empty;
        bool isAsync = false;
        if (path.StartsWithSegments("/async-function"))
        {
            functionBeginPath = "/async-function/";
            isAsync = true;
        }
        else if (path.StartsWithSegments("/function"))
        {
            functionBeginPath = "/function/";
        }
        
        if(!string.IsNullOrEmpty(functionBeginPath))
        {
            var pathString = path.ToUriComponent();
            var paths = path.ToUriComponent().Split("/");
            if(paths.Length > 2) {
                var functionName = paths[2];
                var functionPath = pathString.Replace(functionBeginPath + functionName, "");
                faasLogger.LogInformation($"{method}: {pathString}{queryString.ToUriComponent()}");
                var customRequest = new CustomRequest()
                {
                    Headers = customHeaders,
                    FunctionName = functionName,
                    Path = functionPath,
                    Body = body,
                    Query= queryString.ToUriComponent(),
                    Form = customForms,
                    FormFiles = customFormFiles,
                    Method = method,
                    ContentType = contentType
                };
                
                if (isAsync)
                {
                    _queue.EnqueueAsync(functionName, JsonSerializer.Serialize(customRequest));
                    context.Response.StatusCode = 202;
                    return;
                }
                using var scope = _serviceProvider.CreateScope();
                var response =  await scope.ServiceProvider.GetRequiredService<SendClient>().SendHttpRequestAsync(customRequest);
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
                foreach (var responseHeader in response.Headers)
                {
                    if(responseHeader.Key == "Content-Length")
                        continue;
                    foreach (var value in responseHeader.Value)
                    {
                        context.Response.Headers.Add(responseHeader.Key, value);
                    }
                }
                var bodyResponse = await response.Content.ReadAsStringAsync();
                await context.Response.WriteAsync(bodyResponse);
                return;
            }
        }
        
        await _next(context);
    }
}