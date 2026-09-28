using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CardStatement.Api.Profiles;

public static class ProfileEndpoints
{
    public static void MapProfiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profiles", (IProfileService service) =>
        {
            var profiles = service.ListProfiles();
            return Results.Ok(profiles);
        })
        .DisableAntiforgery();
    }
}
