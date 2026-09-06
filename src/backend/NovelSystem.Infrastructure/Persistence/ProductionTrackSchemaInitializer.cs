using Microsoft.EntityFrameworkCore;

namespace NovelSystem.Infrastructure.Persistence;

/// <summary>多轨制作与声音导演数据结构升级，独立于主初始化器便于旧 SQLite 平滑升级。</summary>
public static class ProductionTrackSchemaInitializer
{
    public static async Task EnsureAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "ProductionTracks" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ProductionTracks" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "Order" INTEGER NOT NULL,
                "Volume" REAL NOT NULL DEFAULT 1,
                "IsMuted" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_ProductionTracks_NovelId_Order" ON "ProductionTracks" ("NovelId","Order");

            CREATE TABLE IF NOT EXISTS "ProductionTrackClips" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ProductionTrackClips" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NOT NULL,
                "TrackId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "FilePath" TEXT NOT NULL,
                "StartMs" INTEGER NOT NULL,
                "EndMs" INTEGER NOT NULL,
                "DurationMs" INTEGER NOT NULL,
                "Volume" REAL NOT NULL DEFAULT 1,
                "FadeInMs" INTEGER NOT NULL DEFAULT 0,
                "FadeOutMs" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_ProductionTrackClips_TrackId_StartMs" ON "ProductionTrackClips" ("TrackId","StartMs");
            CREATE INDEX IF NOT EXISTS "IX_ProductionTrackClips_NovelId_StartMs" ON "ProductionTrackClips" ("NovelId","StartMs");

            CREATE TABLE IF NOT EXISTS "SoundDesignCues" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SoundDesignCues" PRIMARY KEY AUTOINCREMENT,
                "NovelId" INTEGER NOT NULL,
                "ChapterId" INTEGER NULL,
                "ScriptLineId" INTEGER NULL,
                "Type" TEXT NOT NULL,
                "Scene" TEXT NOT NULL,
                "Mood" TEXT NOT NULL,
                "Intensity" INTEGER NOT NULL DEFAULT 50,
                "Keywords" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "StartMs" INTEGER NOT NULL,
                "EndMs" INTEGER NOT NULL,
                "Approved" INTEGER NOT NULL DEFAULT 0,
                "Applied" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_SoundDesignCues_NovelId_ChapterId_StartMs" ON "SoundDesignCues" ("NovelId","ChapterId","StartMs");
            CREATE INDEX IF NOT EXISTS "IX_SoundDesignCues_NovelId_Approved_Applied" ON "SoundDesignCues" ("NovelId","Approved","Applied");
            """, cancellationToken);
    }
}
