using Microsoft.EntityFrameworkCore;

namespace InvoicingTool.Api.Data;

public class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SyncRecord> SyncRecords => Set<SyncRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasIndex(a => a.NormalizedEmail).IsUnique();
            e.Property(a => a.Email).HasMaxLength(256);
            e.Property(a => a.NormalizedEmail).HasMaxLength(256);
            e.HasMany(a => a.Devices).WithOne().HasForeignKey(d => d.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Device>(e =>
        {
            e.ToTable("devices");
            e.Property(d => d.Name).HasMaxLength(100);
            e.Property(d => d.SeriesLetter).HasMaxLength(2);
        });
        model.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.AccountId);
            e.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SyncRecord>(e =>
        {
            e.ToTable("sync_records");
            e.Property(r => r.EntityType).HasMaxLength(32);
            e.Property(r => r.Payload).HasColumnType("jsonb");
            e.HasIndex(r => new { r.AccountId, r.EntityType, r.EntityId }).IsUnique();
            e.HasIndex(r => new { r.AccountId, r.Version });
            e.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
