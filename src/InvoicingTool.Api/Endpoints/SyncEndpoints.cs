using System.Text;
using System.Text.Json;
using InvoicingTool.Api.Auth;
using InvoicingTool.Api.Data;
using InvoicingTool.Sync.Contracts;
using Microsoft.EntityFrameworkCore;

namespace InvoicingTool.Api.Endpoints;

/// <summary>
/// Envoi et réception des modifications. Chaque envoi reçoit des numéros de version croissants par compte ;
/// un appareil demande ensuite « tout ce qui a changé depuis la version N ». En cas de modifications
/// concurrentes du même élément, la dernière reçue par le serveur l'emporte.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var sync = app.MapGroup("/api/sync").RequireAuthorization();

        sync.MapPost("/push", async (PushRequest request, HttpContext http, SyncDbContext db) =>
        {
            var (accountId, deviceId) = TokenService.Identity(http.User);
            var changes = request.Changes ?? [];
            if (changes.Count == 0)
                return Results.Ok(new PushResponse(0, await db.Accounts.Where(a => a.Id == accountId).Select(a => a.SyncVersion).SingleAsync()));
            if (changes.Count > SyncLimits.MaxBatchSize)
                return AuthEndpoints.Problem(StatusCodes.Status400BadRequest, $"Au plus {SyncLimits.MaxBatchSize} éléments par envoi.");
            foreach (var change in changes)
            {
                if (!SyncEntityTypes.All.Contains(change.EntityType))
                    return AuthEndpoints.Problem(StatusCodes.Status400BadRequest, $"Type d'élément inconnu : {change.EntityType}.");
                if (change.Payload.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(change.Payload.GetRawText()) > SyncLimits.MaxPayloadBytes)
                    return AuthEndpoints.Problem(StatusCodes.Status400BadRequest, "Élément invalide ou trop volumineux.");
            }
            // Un même élément envoyé deux fois dans le lot : seule la dernière version compte.
            var latest = changes.GroupBy(c => (c.EntityType, c.EntityId)).Select(g => g.Last()).ToList();

            await using var transaction = await db.Database.BeginTransactionAsync();
            // Réserve un bloc de versions : la ligne du compte est verrouillée jusqu'à la fin de la transaction,
            // ce qui ordonne les envois simultanés de plusieurs appareils.
            // (requête non composable : le résultat est lu tel quel, sans .Single() côté SQL)
            var lastVersion = (await db.Database
                .SqlQuery<long>($"UPDATE accounts SET \"SyncVersion\" = \"SyncVersion\" + {latest.Count} WHERE \"Id\" = {accountId} RETURNING \"SyncVersion\" AS \"Value\"")
                .ToListAsync()).Single();
            var version = lastVersion - latest.Count;

            var ids = latest.Select(c => c.EntityId).ToList();
            var existing = await db.SyncRecords
                .Where(r => r.AccountId == accountId && ids.Contains(r.EntityId))
                .ToDictionaryAsync(r => (r.EntityType, r.EntityId));
            foreach (var change in latest)
            {
                version++;
                if (!existing.TryGetValue((change.EntityType, change.EntityId), out var record))
                {
                    record = new SyncRecord { AccountId = accountId, EntityType = change.EntityType, EntityId = change.EntityId, Payload = "" };
                    db.SyncRecords.Add(record);
                }
                record.Version = version;
                record.UpdatedAt = DateTime.SpecifyKind(change.UpdatedAt, DateTimeKind.Utc);
                record.IsDeleted = change.IsDeleted;
                record.DeviceId = deviceId;
                record.Payload = change.Payload.GetRawText();
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new PushResponse(latest.Count, lastVersion));
        });

        sync.MapGet("/pull", async (long? since, int? limit, HttpContext http, SyncDbContext db) =>
        {
            var (accountId, deviceId) = TokenService.Identity(http.User);
            var from = since ?? 0;
            var take = Math.Clamp(limit ?? SyncLimits.MaxBatchSize, 1, SyncLimits.MaxBatchSize);
            var accountVersion = await db.Accounts.Where(a => a.Id == accountId).Select(a => a.SyncVersion).SingleAsync();

            // Les modifications envoyées par cet appareil ne lui sont pas renvoyées.
            var records = await db.SyncRecords.AsNoTracking()
                .Where(r => r.AccountId == accountId && r.Version > from && r.Version <= accountVersion && r.DeviceId != deviceId)
                .OrderBy(r => r.Version)
                .Take(take + 1)
                .ToListAsync();
            var hasMore = records.Count > take;
            if (hasMore)
                records.RemoveAt(records.Count - 1);

            var changes = records.Select(r => new SyncChange(r.EntityType, r.EntityId, DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc),
                r.IsDeleted, JsonDocument.Parse(r.Payload).RootElement, r.Version)).ToList();
            var next = hasMore ? records[^1].Version : accountVersion;
            return Results.Ok(new PullResponse(changes, next, hasMore));
        });
    }
}
