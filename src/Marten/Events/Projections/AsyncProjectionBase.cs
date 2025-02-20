using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;

namespace Marten.Events.Projections;

// Leave public for codegen!
public abstract class AsyncProjectionBase: IProjection
{
    public abstract Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation);
}
