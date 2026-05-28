using System.Net;

namespace CardStatement.Tests.Apis;

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    public List<string> RequestedUrls { get; } = [];
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestedUrls.Add(request.RequestUri!.ToString());
        return Task.FromResult(_responder(request));
    }
}
