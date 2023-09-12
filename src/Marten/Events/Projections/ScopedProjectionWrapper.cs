#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Events.Projections;

/// <summary>
/// This is used to create projections that utilize scoped or transient
/// IoC services during execution
/// </summary>
/// <typeparam name="TProjection"></typeparam>
internal class ScopedProjectionWrapper<TProjection> : IProjection
    where TProjection : IProjection
{
    private readonly IServiceProvider _serviceProvider;

    public ScopedProjectionWrapper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var projection = sp.GetRequiredService<TProjection>();
        projection.Apply(operations, streams);
    }

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var projection = sp.GetRequiredService<TProjection>();
        await projection.ApplyAsync(operations, streams, cancellation).ConfigureAwait(false);
    }
}
