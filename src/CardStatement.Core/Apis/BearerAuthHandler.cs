using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace CardStatement.Core.Apis;

public sealed class BearerAuthHandler : DelegatingHandler
{
    private readonly IOptions<ApiOptions> _options;

    public BearerAuthHandler(IOptions<ApiOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _options.Value.BearerToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
