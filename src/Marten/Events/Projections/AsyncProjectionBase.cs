using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections;

// Leave public for codegen!
public abstract class AsyncProjectionBase: IProjection
{
    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        throw new NotSupportedException();
    }

    public abstract Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation);

    public bool EnableDocumentTrackingDuringRebuilds { get; set; }
}
