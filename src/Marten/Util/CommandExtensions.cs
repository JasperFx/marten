#nullable enable
using System.Collections.Generic;
using System.Linq;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Util;

internal static class CommandExtensions
{
    public static NpgsqlBatch BuildCommand(this IMartenSession session, ISqlFragment statement)
    {
        var builder = new BatchBuilder(){TenantId = session.TenantId};

        statement.Apply(builder);

        return builder.Compile();
    }

    public static NpgsqlBatch BuildCommand(this IMartenSession session, IQueryHandler handler)
    {
        var builder = new BatchBuilder(){TenantId = session.TenantId};

        handler.ConfigureCommand(builder, session);

        return builder.Compile();
    }

    public static NpgsqlBatch BuildCommand(this IMartenSession session, IEnumerable<IQueryHandler> handlers)
    {
        var builder = new BatchBuilder(){TenantId = session.TenantId};

        foreach (var handler in handlers)
        {
            builder.StartNewCommand();
            handler.ConfigureCommand(builder, session);
        }

        return builder.Compile();
    }
}
