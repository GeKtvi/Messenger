using Microsoft.EntityFrameworkCore;
using Pingo.Messages.Entities;

namespace Pingo.Messages.Data;

// What reading and writing have in common: how messages are stored. It offers no way to reach them;
// MessageDbContext and ReadOnlyMessageDbContext each add their own.
internal abstract class MessageDbContextBase(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(entity =>
        {
            // Named here, because only MessageDbContext has a DbSet to take the name from.
            entity.ToTable("Messages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Text).HasMaxLength(MessageLimits.MaxTextLength).IsRequired();
            entity.HasIndex(m => m.SentAt);
        });
    }
}
