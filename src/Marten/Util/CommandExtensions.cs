#nullable enable
using System.Collections.Generic;
using System.Linq;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration;
using Marten.Schema.Arguments;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Util;

internal static class CommandExtensions
{
    public static NpgsqlCommand BuildCommand(this IMartenSession session, Statement statement)
    {
        var command = new NpgsqlCommand();
        var builder = new CommandBuilder(command);

        statement.Configure(builder);

        command.CommandText = builder.ToString();

        session.TrySetTenantId(command);

        return command;
    }

    public static void TrySetTenantId(this IMartenSession session, NpgsqlCommand command)
    {
        var tenantParameter = command.Parameters.FirstOrDefault(x => x.ParameterName == TenantIdArgument.ArgName);

        if (tenantParameter != null)
        {
            tenantParameter.Value = session.TenantId;
        }
    }

    public static NpgsqlCommand BuildCommand(this IMartenSession session, IQueryHandler handler)
    {
        var command = new NpgsqlCommand();
        var builder = new CommandBuilder(command);

        handler.ConfigureCommand(builder, session);

        command.CommandText = builder.ToString();

        session.TrySetTenantId(command);

        return command;
    }

    public static NpgsqlCommand BuildCommand(this IMartenSession session, IEnumerable<IQueryHandler> handlers)
    {
        var command = new NpgsqlCommand();
        var builder = new CommandBuilder(command);

        foreach (var handler in handlers)
        {
            handler.ConfigureCommand(builder, session);
            builder.Append(";");
        }

        command.CommandText = builder.ToString();

        session.TrySetTenantId(command);

        return command;
    }
}
