#nullable enable
using System.Collections.Generic;
using JasperFx.Core.Reflection;
using Marten.Internal.Operations;
using Npgsql;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Internal.Sessions;

public class OperationPage: OperationPage<IMartenSession, ICommandBuilder, IStorageOperation>
{
    public OperationPage(IMartenSession session): base(session, new BatchBuilder())
    {
    }

    public OperationPage(IMartenSession session, IReadOnlyList<IStorageOperation> operations): base(session,
        new BatchBuilder(), operations)
    {
    }

    public NpgsqlBatch Compile()
    {
        // TODO -- smelly, and won't leave it like this
        return _builder.As<BatchBuilder>().Compile();
    }
}
