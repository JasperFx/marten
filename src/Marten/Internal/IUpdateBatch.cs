using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace Marten.Internal
{
    public interface IUpdateBatch
    {
        string? TenantId { get; }
        void ApplyChanges(IMartenSession session);
        Task ApplyChangesAsync(IMartenSession session, CancellationToken token);
    }
}
