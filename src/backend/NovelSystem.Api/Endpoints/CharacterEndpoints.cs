using NovelSystem.Api.Contracts;
using NovelSystem.Infrastructure.Persistence;

namespace NovelSystem.Api.Endpoints;

/// <summary>小说人物及音色配置 API。</summary>
public static class CharacterEndpoints
{
    public static IEndpointRouteBuilder MapCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/characters/{id:long}", async (long id, UpdateCharacterRequest request, AppDbContext db) =>
        {
            var character = await db.Characters.FindAsync(id);
            if (character is null) return Results.NotFound();

            character.Name = request.Name;
            character.Gender = request.Gender;
            character.Personality = request.Personality;
            character.Description = request.Description;
            character.VoiceProfileId = request.VoiceProfileId;
            character.VoiceFile = request.VoiceFile;
            await db.SaveChangesAsync();
            return Results.Ok(character);
        }).WithTags("Characters");

        return app;
    }
}