using Microsoft.EntityFrameworkCore;
using Pingo.Messages.Entities;

namespace Pingo.Messages.Data;

// The primary database: all writes, and the migrations.
internal class MessageDbContext(DbContextOptions<MessageDbContext> options) : MessageDbContextBase(options)
{
    public DbSet<Message> Messages => Set<Message>();
}
