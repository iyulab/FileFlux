using FileFlux.SampleApp.Models;
using Microsoft.EntityFrameworkCore;

namespace FileFlux.SampleApp.Data;

/// <summary>
/// FileFlux SQLite 데이터베이스 컨텍스트
/// </summary>
public class FileFluxDbContext : DbContext
{
    public FileFluxDbContext(DbContextOptions<FileFluxDbContext> options) : base(options)
    {
    }

    public DbSet<DocumentRecord> Documents { get; set; } = null!;
    public DbSet<ChunkRecord> Chunks { get; set; } = null!;
    public DbSet<QueryRecord> Queries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // DocumentRecord 설정
        modelBuilder.Entity<DocumentRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.FileHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ChunkingStrategy).HasMaxLength(100);
            entity.HasIndex(e => e.FileHash).IsUnique();
            entity.HasIndex(e => e.FilePath);
        });

        // ChunkRecord 설정
        modelBuilder.Entity<ChunkRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.HasIndex(e => new { e.DocumentId, e.Order });

            entity.HasOne(e => e.Document)
                  .WithMany(e => e.Chunks)
                  .HasForeignKey(e => e.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // QueryRecord 설정
        modelBuilder.Entity<QueryRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Query).IsRequired().HasMaxLength(2000);
            entity.HasIndex(e => e.QueryTime);
        });
    }
}