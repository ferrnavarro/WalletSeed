using System.Globalization;
using System.Net.Http.Json;
using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;
using Microsoft.Extensions.Options;

namespace CardStatement.Core.Apis;

public sealed class CategoryApiClient : ICategoryApi
{
    private readonly HttpClient _http;
    private readonly ApiOptions _options;

    public CategoryApiClient(HttpClient http, IOptions<ApiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default)
    {
        var all = new List<Category>();
        var offset = 0;
        var limit = _options.PageSize;

        while (true)
        {
            var page = await GetPageAsync(offset, limit, ct).ConfigureAwait(false);

            foreach (var c in page.Categories)
            {
                if (string.IsNullOrWhiteSpace(c.Name)) continue;
                all.Add(new Category(c.Id, c.Name!, c.Color, c.EnvelopeId, c.Cardinality));
            }

            if (page.NextOffset is int next && next > offset && page.Categories.Length > 0)
            {
                offset = next;
                continue;
            }

            if (page.Categories.Length >= limit)
            {
                offset += limit;
                continue;
            }

            break;
        }

        return all;
    }

    private async Task<CategoryPageDto> GetPageAsync(int offset, int limit, CancellationToken ct)
    {
        var url = $"categories?agentHints=true&limit={limit.ToString(CultureInfo.InvariantCulture)}&offset={offset.ToString(CultureInfo.InvariantCulture)}";
        var page = await _http.GetFromJsonAsync<CategoryPageDto>(url, ct).ConfigureAwait(false);
        return page ?? throw new InvalidOperationException("Empty category page response.");
    }
}
