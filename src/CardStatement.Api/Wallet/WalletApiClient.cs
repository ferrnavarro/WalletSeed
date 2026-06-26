using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CardStatement.Api.Wallet;

public sealed class WalletApiClient : IWalletApiClient
{
    private readonly HttpClient _httpClient;
    private readonly WalletOptions _options;
    private readonly ILogger<WalletApiClient> _logger;

    public WalletApiClient(HttpClient httpClient, IOptions<WalletOptions> options, ILogger<WalletApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        var timeoutSeconds = _options.TimeoutSeconds >= 1 ? _options.TimeoutSeconds : 30;
        if (_options.TimeoutSeconds < 1)
        {
            _logger.LogWarning("Wallet timeoutSeconds {TimeoutSeconds} is invalid; using 30 seconds instead.", _options.TimeoutSeconds);
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public async Task<IReadOnlyList<WalletAccount>> ListAccountsAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        var response = await SendAsync(HttpMethod.Get, "accounts", ct);
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return document?.RootElement.GetProperty("accounts").EnumerateArray().Select(a => new WalletAccount(
            a.GetProperty("id").GetString() ?? string.Empty,
            a.GetProperty("name").GetString() ?? string.Empty,
            ReadCurrencyCode(a),
            a.GetProperty("accountType").GetString() ?? string.Empty,
            a.TryGetProperty("archived", out var archived) && archived.GetBoolean())).ToList() ?? [];
    }

    public async Task<IReadOnlyList<WalletCategory>> ListCategoriesAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        var response = await SendAsync(HttpMethod.Get, "categories", ct);
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return document?.RootElement.GetProperty("categories").EnumerateArray().Select(c => new WalletCategory(
            c.GetProperty("id").GetString() ?? string.Empty,
            c.GetProperty("name").GetString() ?? string.Empty,
            c.TryGetProperty("color", out var color) ? color.GetString() : null)).ToList() ?? [];
    }

    public async Task<IReadOnlyList<WalletRecord>> ListRecordsAsync(string accountId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        EnsureConfigured();
        var records = new List<WalletRecord>();
        var offset = 0;
        while (true)
        {
            var requestUri = $"records?accountId={Uri.EscapeDataString(accountId)}&recordDate=gte.{from:yyyy-MM-dd}&recordDate=lt.{to.AddDays(1):yyyy-MM-dd}&limit=200&offset={offset}";
            var response = await SendAsync(HttpMethod.Get, requestUri, ct);
            var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var items = document?.RootElement.GetProperty("records").EnumerateArray().Select(r => new WalletRecord(
                r.GetProperty("id").GetString() ?? string.Empty,
                DateOnly.Parse(r.GetProperty("recordDate").GetString() ?? string.Empty),
                r.GetProperty("amount").GetProperty("value").GetDecimal(),
                r.GetProperty("amount").GetProperty("currencyCode").GetString() ?? string.Empty,
                r.TryGetProperty("note", out var note) ? note.GetString() : null,
                r.TryGetProperty("counterParty", out var counterParty) ? counterParty.GetString() : null,
                r.TryGetProperty("categoryName", out var categoryName) ? categoryName.GetString() : null)).ToList() ?? [];
            records.AddRange(items);

            int? nextOffset = document?.RootElement.TryGetProperty("nextOffset", out var next) == true ? next.GetInt32() : null;
            if (nextOffset is null)
            {
                break;
            }

            offset = nextOffset.Value;
        }

        return records;
    }

    public async Task<IReadOnlyList<WalletLabel>> ListLabelsAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        var labels = new List<WalletLabel>();
        var offset = 0;
        while (true)
        {
            var requestUri = $"labels?limit=200&offset={offset}";
            var response = await SendAsync(HttpMethod.Get, requestUri, ct);
            var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            var items = document?.RootElement.GetProperty("labels").EnumerateArray().Select(l => new WalletLabel(
                l.GetProperty("id").GetString() ?? string.Empty,
                l.GetProperty("name").GetString() ?? string.Empty,
                l.TryGetProperty("color", out var color) ? color.GetString() : null,
                l.TryGetProperty("archived", out var archived) && archived.GetBoolean())).ToList() ?? [];
            labels.AddRange(items);

            int? nextOffset = document?.RootElement.TryGetProperty("nextOffset", out var next) == true ? next.GetInt32() : null;
            if (nextOffset is null)
            {
                break;
            }

            offset = nextOffset.Value;
        }

        return labels;
    }

    public async Task<IReadOnlyList<WalletCreateOutcome>> CreateRecordsAsync(IReadOnlyList<WalletCreateRequest> rows, CancellationToken ct = default)
    {
        EnsureConfigured();
        var payload = new { records = rows.Select(r => new { accountId = r.AccountId, recordDate = r.RecordDate.ToString("O"), amount = new { value = r.SignedAmount, currencyCode = r.CurrencyCode }, paymentType = r.PaymentType, categoryId = r.CategoryId, labelIds = r.LabelIds, note = r.Note, counterParty = r.CounterParty }).ToList() };
        var response = await SendAsync(HttpMethod.Post, "records", ct, payload);
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        return document?.RootElement.GetProperty("outcomes").EnumerateArray().Select((item, index) => new WalletCreateOutcome(index, item.GetProperty("success").GetBoolean(), item.TryGetProperty("id", out var id) ? id.GetString() : null, item.TryGetProperty("error", out var error) ? error.GetString() : null)).ToList() ?? [];
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl) || !Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new WalletApiException(WalletApiErrorKind.NotConfigured, "Wallet API base URL is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Jwt))
        {
            throw new WalletApiException(WalletApiErrorKind.NotConfigured, "Wallet API JWT is not configured.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken ct, object? payload = null)
    {
        EnsureConfigured();

        var baseUri = _httpClient.BaseAddress ?? new Uri(_options.BaseUrl!, UriKind.Absolute);
        var requestUri = CombineUri(baseUri, path);
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Jwt);
        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        }

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new WalletApiException(WalletApiErrorKind.CredentialsInvalid, "Wallet credentials are invalid.", (int)response.StatusCode, await ReadBodyExcerptAsync(response));
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new WalletApiException(WalletApiErrorKind.Unavailable, "Wallet API is unavailable.", (int)response.StatusCode, await ReadBodyExcerptAsync(response));
            }

            if ((int)response.StatusCode >= 400)
            {
                throw new WalletApiException(WalletApiErrorKind.Rejected, "Wallet request was rejected.", (int)response.StatusCode, await ReadBodyExcerptAsync(response));
            }

            return response;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Wallet API request timed out. BodyExcerpt: {BodyExcerpt}", "<timeout>");
            throw new WalletApiException(WalletApiErrorKind.Unavailable, "Wallet API request timed out.", null, "<timeout>");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Wallet API request failed. BodyExcerpt: {BodyExcerpt}", "<network>");
            throw new WalletApiException(WalletApiErrorKind.Unavailable, "Wallet API is unavailable.", null, "<network>");
        }
    }

    private static Uri CombineUri(Uri baseUri, string path)
    {
        var normalizedBase = baseUri.ToString().TrimEnd('/') + "/";
        var normalizedPath = path.TrimStart('/');
        return new Uri(new Uri(normalizedBase, UriKind.Absolute), normalizedPath);
    }

    private static string ReadCurrencyCode(JsonElement account)
    {
        if (account.TryGetProperty("currencyCode", out var currencyCode) && currencyCode.ValueKind == JsonValueKind.String)
        {
            return currencyCode.GetString() ?? string.Empty;
        }

        if (account.TryGetProperty("initialBalance", out var initialBalance) &&
            initialBalance.ValueKind == JsonValueKind.Object &&
            initialBalance.TryGetProperty("currencyCode", out var initialCurrencyCode) &&
            initialCurrencyCode.ValueKind == JsonValueKind.String)
        {
            return initialCurrencyCode.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static async Task<string?> ReadBodyExcerptAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            if (body.Length > 256)
            {
                return body[..256];
            }

            return body;
        }
        catch
        {
            return null;
        }
    }
}
