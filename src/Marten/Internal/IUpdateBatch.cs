#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Weasel.Core.Operations;

namespace Marten.Internal;

public interface IUpdateBatch
{
    IReadOnlyList<OperationPage> BuildPages(IMartenSession session);

    IReadOnlyList<Type> DocumentTypes();
    Task PostUpdateAsync(IMartenSession session);
    Task PreUpdateAsync(IMartenSession session);
}
