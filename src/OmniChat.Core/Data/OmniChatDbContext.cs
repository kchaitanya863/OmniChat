using Microsoft.EntityFrameworkCore;
using OmniChat.Core.Data.Entities;

namespace OmniChat.Core.Data;

public sealed class OmniChatDbContext : DbContext
{
    public OmniChatDbContext(DbContextOptions<OmniChatDbContext> options) : base(options) { }

    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<ProviderConfigEntity> ProviderConfigs => Set<ProviderConfigEntity>();
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<ChunkEntity> Chunks => Set<ChunkEntity>();
    public DbSet<PersonaEntity> Personas => Set<PersonaEntity>();
    public DbSet<FolderEntity> Folders => Set<FolderEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SessionEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Title).HasMaxLength(512).IsRequired();
            e.HasIndex(s => s.UpdatedAt);
            e.HasIndex(s => s.FolderId);
            e.HasMany(s => s.Messages).WithOne(m => m.Session).HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MessageEntity>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Role).HasMaxLength(32).IsRequired();
            e.Property(m => m.Content).IsRequired();
            e.HasIndex(m => m.SessionId);
            e.HasIndex(m => new { m.SessionId, m.Timestamp });
            e.HasIndex(m => m.ParentId);
        });

        b.Entity<ProviderConfigEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(p => p.Model).HasMaxLength(200).IsRequired();
            e.HasIndex(p => p.UpdatedAt);
        });

        b.Entity<DocumentEntity>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(512).IsRequired();
            e.Property(d => d.Sha256).HasMaxLength(64);
            e.HasIndex(d => d.Sha256);
            e.HasMany(d => d.Chunks).WithOne(c => c.Document).HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChunkEntity>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.DocumentId);
            e.HasIndex(c => new { c.DocumentId, c.Sequence });
            e.Property(c => c.Content).IsRequired();
        });

        b.Entity<PersonaEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(120).IsRequired();
            e.Property(p => p.SystemPrompt).IsRequired();
        });

        b.Entity<FolderEntity>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Name).HasMaxLength(120).IsRequired();
        });
    }
}
