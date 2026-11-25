using Microsoft.EntityFrameworkCore;
using Pingo.Messages.Entities;

namespace Pingo.Messages.Data;

// The read replica: queries only. It offers no DbSet to add to, tracks nothing, and refuses to save.
internal class ReadOnlyMessageDbContext(DbContextOptions<ReadOnlyMessageDbContext> options) : MessageDbContextBase(options)
{
    public IQueryable<Message> Messages => Set<Message>().AsNoTracking();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    private static InvalidOperationException ReadOnly() =>
        new($"{nameof(ReadOnlyMessageDbContext)} is for reading only; write through {nameof(MessageDbContext)}.");
}
