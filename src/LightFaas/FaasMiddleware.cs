using System.Text.Json;
using WebApplication1;

public class FaasMiddleware 
{
    private readonly RequestDelegate _next;
    private readonly SendClient _sendClient;
    private readonly IQueue _queue;
    private HttpResponse _contextResponse;

    public FaasMiddleware(RequestDelegate next,SendClient sendClient, IQueue queue)
    {
        _next = next;
        _sendClient = sendClient;
        _queue = queue;
    }

    public async Task InvokeAsync(HttpContext context)
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
        if (method == "POST")
        {
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
                Console.WriteLine(functionName);
                var functionPath = pathString.Replace(functionBeginPath + functionName, "");
                Console.WriteLine(functionPath);
                var customRequest = new CustomRequest()
                {
                    Headers = customHeaders,
                    FunctionName = functionName,
                    Path = functionPath,
                    Query= queryString.ToUriComponent(),
                    Form = customForms,
                    FormFiles = customFormFiles,
                    Method = method
                };
                _contextResponse = context.Response;
                if (isAsync)
                {
                    _queue.EnqueueAsync(functionName, JsonSerializer.Serialize(customRequest));
                    _contextResponse.StatusCode = 202;
                    return;
                }

                var response = await _sendClient.SendHttpRequestAsync(customRequest);
                
                _contextResponse.StatusCode = (int)response.StatusCode;
                _contextResponse.ContentType = response.Content.Headers.ContentType?.ToString();
                foreach (var responseHeader in response.Headers)
                {
                    if(responseHeader.Key == "Content-Length")
                        continue;
                    foreach (var value in responseHeader.Value)
                    {
                        _contextResponse.Headers.Add(responseHeader.Key, value);
                    }
                }
                var body = await response.Content.ReadAsStringAsync();
                await _contextResponse.WriteAsync(body);
                return;
            }
        }

        // Do work that can write to the Response.
        await _next(context);
    }
}