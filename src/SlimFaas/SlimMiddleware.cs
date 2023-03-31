using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SlimFaas;

public class SlimMiddleware 
{
    private readonly RequestDelegate _next;
    private readonly IQueue _queue;
    private HttpContent _responseContent;

    public SlimMiddleware(RequestDelegate next, IQueue queue)
    {
        _next = next;
        _queue = queue;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    public async Task InvokeAsync(HttpContext context, ILogger<SlimMiddleware> faasLogger, HistoryHttpMemoryService historyHttpService, SendClient sendClient)
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

                var formFileCollection = contextRequest.Form.Files;
                if (formFileCollection.Count > 0)
                {
                    foreach (var formFile in formFileCollection)
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
        var isAsync = false;
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
            var paths = pathString.Split("/");
            if(paths.Length > 2) {
                var functionName = paths[2];
                var functionPath = pathString.Replace(functionBeginPath + functionName, "");
                faasLogger.LogInformation("{Method}: {PathString}{UriComponent}", method, pathString, queryString.ToUriComponent());
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

                var contextResponse = context.Response;
                if (isAsync)
                {
                    _queue.EnqueueAsync(functionName, JsonSerializer.Serialize(customRequest));
                    contextResponse.StatusCode = 202;
                    return;
                }
                historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
                var response =  await sendClient.SendHttpRequestAsync(customRequest);
                historyHttpService.SetTickLastCall(functionName, DateTime.Now.Ticks);
                contextResponse.StatusCode = (int)response.StatusCode;
                _responseContent = response.Content;
                contextResponse.ContentType = _responseContent.Headers.ContentType?.ToString();
                foreach (var responseHeader in response.Headers)
                {
                    if(responseHeader.Key == "Content-Length")
                        continue;
                    foreach (var value in responseHeader.Value)
                    {
                        contextResponse.Headers.Add(responseHeader.Key, value);
                    }
                }
                var bodyResponse = await _responseContent.ReadAsStringAsync();
                await contextResponse.WriteAsync(bodyResponse);
                return;
            }
        }
        
        await _next(context);
    }
}