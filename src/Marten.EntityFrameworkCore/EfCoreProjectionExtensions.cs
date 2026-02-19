using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Microsoft.EntityFrameworkCore;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering EF Core projections with Marten.
/// </summary>
public static class EfCoreProjectionExtensions
{
    /// <summary>
    /// Register an <see cref="EfCoreEventProjection{TDbContext}"/> with Marten.
    /// </summary>
    public static void Add<TProjection>(this ProjectionOptions projections,
        ProjectionLifecycle lifecycle)
        where TProjection : IProjection, new()
    {
        projections.Add(new TProjection(), lifecycle);
    }
}
