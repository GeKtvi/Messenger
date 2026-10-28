using Microsoft.EntityFrameworkCore;
using Pingo.Messages.Entities;

namespace Pingo.Messages.Data;

internal class MessageDbContext(DbContextOptions<MessageDbContext> options) : DbContext(options)
{
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Text).HasMaxLength(MessageLimits.MaxTextLength).IsRequired();
            entity.HasIndex(m => m.SentAt);
        });
    }
}
