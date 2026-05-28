using System.Globalization;
using System.Net.Http.Json;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;
using Microsoft.Extensions.Options;

namespace CardStatement.Core.Apis;

public sealed class LabelApiClient : ILabelsApi
{
    private readonly HttpClient _http;
    private readonly ApiOptions _options;

    public LabelApiClient(HttpClient http, IOptions<ApiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<Label>> GetAllAsync(CancellationToken ct = default)
    {
        var all = new List<Label>();
        var offset = 0;
        var limit = _options.PageSize;

        while (true)
        {
            var page = await GetPageAsync(offset, limit, ct).ConfigureAwait(false);
            foreach (var l in page.Labels)
            {
                if (l.Archived) continue;
                if (string.IsNullOrWhiteSpace(l.Name)) continue;
                all.Add(new Label(l.Id, l.Name!, l.Color, l.Archived));
            }
            if (page.Labels.Length < limit) break;
            offset += limit;
        }

        return all;
    }

    private async Task<LabelPageDto> GetPageAsync(int offset, int limit, CancellationToken ct)
    {
        var url = $"labels?limit={limit.ToString(CultureInfo.InvariantCulture)}&offset={offset.ToString(CultureInfo.InvariantCulture)}";
        var page = await _http.GetFromJsonAsync<LabelPageDto>(url, ct).ConfigureAwait(false);
        return page ?? throw new InvalidOperationException("Empty label page response.");
    }
}
