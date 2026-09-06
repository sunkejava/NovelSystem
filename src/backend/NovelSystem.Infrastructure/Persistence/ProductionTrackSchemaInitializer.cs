using Microsoft.EntityFrameworkCore;

namespace NovelSystem.Infrastructure.Persistence;

/// <summary>第三阶段多轨制作数据结构升级，独立于主初始化器便于旧 SQLite 平滑升级。</summary>
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
            """, cancellationToken);
    }
}
