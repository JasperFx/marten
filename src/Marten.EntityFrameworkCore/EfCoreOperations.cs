using Microsoft.EntityFrameworkCore;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Provides simultaneous access to Marten's <see cref="IDocumentOperations"/> and an
/// EF Core <see cref="DbContext"/> within a projection. Both sets of writes will be
/// committed atomically in the same database transaction.
/// </summary>
public class EfCoreOperations<TDbContext> where TDbContext : DbContext
{
    public IDocumentOperations Marten { get; }
    public TDbContext DbContext { get; }

    internal EfCoreOperations(IDocumentOperations marten, TDbContext dbContext)
    {
        Marten = marten;
        DbContext = dbContext;
    }
}
