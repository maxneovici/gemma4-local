using Microsoft.EntityFrameworkCore;

namespace LLLMax.Api.Storage;

public static class JsonImport
{
    public static async Task<bool> ShouldImportAsync(LocalDbContext db, string marker, CancellationToken cancellationToken)
    {
        var existing = await db.AppMetadata.FindAsync([marker], cancellationToken);
        return existing?.Value != "true";
    }

    public static async Task MarkImportedAsync(LocalDbContext db, string marker, CancellationToken cancellationToken)
    {
        var existing = await db.AppMetadata.FindAsync([marker], cancellationToken);

        if (existing is null)
        {
            db.AppMetadata.Add(new AppMetadataEntity { Key = marker, Value = "true" });
        }
        else
        {
            existing.Value = "true";
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task ImportOnceAsync(
        IDbContextFactory<LocalDbContext> dbFactory,
        SemaphoreSlim gate,
        Func<bool> isImported,
        Action markRuntimeImported,
        string marker,
        Func<LocalDbContext, CancellationToken, Task> import,
        CancellationToken cancellationToken)
    {
        if (isImported())
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);

        try
        {
            if (isImported())
            {
                return;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (await ShouldImportAsync(db, marker, cancellationToken))
            {
                await import(db, cancellationToken);
                await MarkImportedAsync(db, marker, cancellationToken);
            }

            markRuntimeImported();
        }
        finally
        {
            gate.Release();
        }
    }
}
