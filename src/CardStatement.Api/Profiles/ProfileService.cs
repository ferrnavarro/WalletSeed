using Microsoft.Extensions.Configuration;

namespace CardStatement.Api.Profiles;

public interface IProfileService
{
    IReadOnlyList<ProfileDto> ListProfiles();
    ProfileSettings? GetProfile(string? profileId);
}

public sealed class ProfileService : IProfileService
{
    private readonly IConfiguration _configuration;

    public ProfileService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IReadOnlyList<ProfileDto> ListProfiles()
    {
        var profilesSection = _configuration.GetSection("Profiles");
        var children = profilesSection.GetChildren();

        var result = new List<ProfileDto>();
        foreach (var child in children)
        {
            var id = child.Key;
            var name = child.GetValue<string>("Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = id;
            }

            result.Add(new ProfileDto(id, name));
        }

        return result;
    }

    public ProfileSettings? GetProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        var profilesSection = _configuration.GetSection("Profiles");
        foreach (var child in profilesSection.GetChildren())
        {
            if (string.Equals(child.Key, profileId, StringComparison.OrdinalIgnoreCase))
            {
                var settings = new ProfileSettings
                {
                    Name = child.GetValue<string>("Name") ?? child.Key,
                    WalletApiToken = child.GetValue<string>("WalletApiToken")
                };

                var mappingSection = child.GetSection("LabelMapping");
                foreach (var mapChild in mappingSection.GetChildren())
                {
                    if (!string.IsNullOrWhiteSpace(mapChild.Key) && !string.IsNullOrWhiteSpace(mapChild.Value))
                    {
                        settings.LabelMapping[mapChild.Key] = mapChild.Value;
                    }
                }

                return settings;
            }
        }

        return null;
    }
}
