public record CustomRequest
{
    public IList<CustomHeader> Headers { get; init; }
    public IList<CustomForm> Form { get; init; }
    public IList<CustomFormFile> FormFiles { get; init; }
    public string FunctionName { get; init; }
    public string Path { get; init; }
    public string Method { get; init; }
    public string Query { get; set; }
    public string Body { get; set; }
    public string ContentType { get; set; }
}



public record CustomHeader
{
    public string Key { get; init; }
    public string?[] Values { get; init; } 
}

public record CustomForm
{
    public string Key { get; init; }
    public string?[] Values { get; init; } 
}


public record CustomFormFile
{
    public string Key { get; init; }
    public byte[] Value { get; init; }
    public string Filename { get; set; }
} 