#nullable enable
using System;
using System.Data.Common;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Storage;

namespace Marten.Internal.Operations;

/// <summary>
///     What <c>QueueSqlCommand</c> enrolls in the unit of work. The placeholder parsing, the
///     parameter-count check and the trailing-<c>;</c> trim are Weasel.Storage's
///     <see cref="QueuedSqlOperation" /> (#5733); Marten keeps the <c>NpgsqlDbType</c> it infers from
///     each value's CLR type, and reports <see cref="StorageFeatures" /> as its document type.
/// </summary>
internal class ExecuteSqlStorageOperation: QueuedSqlOperation, IStorageOperation
{
    public ExecuteSqlStorageOperation(char placeholder, string commandText, params object[] parameterValues)
        : base(commandText, placeholder, parameterValues)
    {
        DocumentType = typeof(StorageFeatures);
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        base.ConfigureCommand(builder, session);
    }

    protected override void BindParameter(DbParameter parameter, object? value)
    {
        if (value != null && parameter is NpgsqlParameter npgsqlParameter)
        {
            var dbType = PostgresqlProvider.Instance.TryGetDbType(value.GetType());
            if (dbType != null)
            {
                npgsqlParameter.NpgsqlDbType = dbType.Value;
            }
        }

        base.BindParameter(parameter, value);
    }
}
