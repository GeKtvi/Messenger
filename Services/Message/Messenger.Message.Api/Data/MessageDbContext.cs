using Messenger.Message.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Message.Api.Data;

public class MessageDbContext(DbContextOptions<MessageDbContext> options) : DbContext(options)
{
    public const int MaxTextLength = 2000;

    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Text).HasMaxLength(MaxTextLength).IsRequired();
            entity.HasIndex(m => m.SentAt);
        });
    }
}
