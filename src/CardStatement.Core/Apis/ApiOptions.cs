namespace CardStatement.Core.Apis;

public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = "";
    public string BearerToken { get; set; } = "";
    public int PageSize { get; set; } = 30;
}
