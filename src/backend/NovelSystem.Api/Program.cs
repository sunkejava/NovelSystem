using NovelSystem.Api.Endpoints;
using NovelSystem.Infrastructure;
using NovelSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Data Source=data/novelsystem.db";

builder.Services.AddNovelSystemInfrastructure(connectionString);
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

Directory.CreateDirectory("data");
Directory.CreateDirectory("storage/audio");
Directory.CreateDirectory("storage/output");
Directory.CreateDirectory("storage/prompts");
Directory.CreateDirectory("storage/production");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
    await ProductionTrackSchemaInitializer.EnsureAsync(db);
}

app.UseCors();
app.MapOpenApi();
app.MapNovelEndpoints();
app.MapCharacterEndpoints();
app.MapVoiceProfileEndpoints();
app.MapJobEndpoints();
app.MapSettingEndpoints();
app.MapWritingEndpoints();
app.MapAudioEndpoints();
app.MapTokenUsageEndpoints();
app.MapAnalysisDiagnosticsEndpoints();
app.MapProductionEndpoints();
app.MapProductionStageThreeEndpoints();
app.MapSoundDesignEndpoints();
app.Run();