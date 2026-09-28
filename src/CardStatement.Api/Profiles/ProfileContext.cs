using CardStatement.Api.Wallet;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CardStatement.Api.Profiles;

public interface IProfileContext
{
    string? ActiveProfileId { get; }
    ProfileSettings? ActiveProfile { get; }
    string? GetActiveWalletApiToken();
    IReadOnlyDictionary<string, string> GetActiveLabelMapping();
}

public sealed class ProfileContext : IProfileContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IProfileService _profileService;
    private readonly WalletOptions _walletOptions;

    private bool _initialized;
    private string? _activeProfileId;
    private ProfileSettings? _activeProfile;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public ProfileContext(
        IHttpContextAccessor httpContextAccessor,
        IProfileService profileService,
        IOptions<WalletOptions> walletOptions)
    {
        _httpContextAccessor = httpContextAccessor;
        _profileService = profileService;
        _walletOptions = walletOptions.Value;
    }

    private ProfileContext(
        IProfileService profileService,
        IOptions<WalletOptions> walletOptions,
        string? activeProfileId)
    {
        _httpContextAccessor = null;
        _profileService = profileService;
        _walletOptions = walletOptions.Value;
        _activeProfileId = activeProfileId;
        if (!string.IsNullOrWhiteSpace(activeProfileId))
        {
            _activeProfile = _profileService.GetProfile(activeProfileId);
        }
        _initialized = true;
    }

    /// <summary>
    /// Test helper factory without HttpContextAccessor.
    /// </summary>
    public static ProfileContext CreateForTest(
        IProfileService profileService,
        IOptions<WalletOptions> walletOptions,
        string? activeProfileId = null)
    {
        return new ProfileContext(profileService, walletOptions, activeProfileId);
    }

    public string? ActiveProfileId
    {
        get
        {
            EnsureInitialized();
            return _activeProfileId;
        }
    }

    public ProfileSettings? ActiveProfile
    {
        get
        {
            EnsureInitialized();
            return _activeProfile;
        }
    }

    public string? GetActiveWalletApiToken()
    {
        EnsureInitialized();
        if (_activeProfile is not null && !string.IsNullOrWhiteSpace(_activeProfile.WalletApiToken))
        {
            return _activeProfile.WalletApiToken;
        }

        return _walletOptions.Jwt;
    }

    public IReadOnlyDictionary<string, string> GetActiveLabelMapping()
    {
        EnsureInitialized();
        var merged = new Dictionary<string, string>(_walletOptions.LabelMapping, StringComparer.OrdinalIgnoreCase);

        if (_activeProfile is not null)
        {
            foreach (var kvp in _activeProfile.LabelMapping)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        return merged;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        if (httpContext.Request.Headers.TryGetValue("X-Profile", out var profileHeader) && !string.IsNullOrWhiteSpace(profileHeader))
        {
            _activeProfileId = profileHeader.ToString().Trim();
        }
        else if (httpContext.Request.Headers.TryGetValue("X-Wallet-Profile", out var walletProfileHeader) && !string.IsNullOrWhiteSpace(walletProfileHeader))
        {
            _activeProfileId = walletProfileHeader.ToString().Trim();
        }

        if (!string.IsNullOrWhiteSpace(_activeProfileId))
        {
            _activeProfile = _profileService.GetProfile(_activeProfileId);
        }
    }
}
