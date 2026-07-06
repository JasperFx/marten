#nullable enable
using System;
using System.Data.Common;
using Marten.Linq.SqlGeneration.Filters;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.Storage;

/// <summary>
///     #4828: the Postgres implementation of <see cref="IStorageDialect"/> — the single place the
///     movable <see cref="DocumentStorage{T,TId}"/> hierarchy's Npgsql command/parameter, id-filter,
///     and Postgres error-code specifics live. Stateless per closed <c>TId</c>, so it is exposed as a
///     per-<c>TId</c> singleton via <see cref="Instance"/>.
/// </summary>
internal sealed class PostgresStorageDialect<TId>: IStorageDialect
{
    public static readonly IStorageDialect Instance = new PostgresStorageDialect<TId>();

    // The id-column parameter type — same lookup the storage ctor used for its _idType.
    private static readonly NpgsqlDbType IdParameterType = PostgresqlProvider.Instance.ToParameterType(typeof(TId));

    private PostgresStorageDialect()
    {
    }

    public DbCommand BuildLoadCommand(string loaderSql, object rawId, string? tenant)
    {
        var command = new NpgsqlCommand(loaderSql);
        command.Parameters.Add(new NpgsqlParameter { Value = rawId });
        if (tenant is not null)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = tenant });
        }

        return command;
    }

    public DbParameter CreateIdArrayParameter(Array rawIds, Type rawSqlType)
        => new NpgsqlParameter
        {
            Value = rawIds,
            NpgsqlDbType = NpgsqlDbType.Array | PostgresqlProvider.Instance.ToParameterType(rawSqlType)
        };

    public DbCommand BuildLoadManyCommand(string loadArraySql, DbParameter idArrayParameter, string? tenant)
    {
        var command = new NpgsqlCommand(loadArraySql);
        command.Parameters.Add(idArrayParameter);
        if (tenant is not null)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = tenant });
        }

        return command;
    }

    public ISqlFragment ByIdFilter(object rawId) => new ByIdFilter(rawId, IdParameterType);

    public bool IsUndefinedTable(Exception exception)
        => exception is PostgresException pg && pg.SqlState == PostgresErrorCodes.UndefinedTable;

    public void SetParameterType(DbParameter parameter, StorageColumnType type)
        => ((NpgsqlParameter)parameter).NpgsqlDbType = type switch
        {
            StorageColumnType.String => NpgsqlDbType.Varchar,
            StorageColumnType.Guid => NpgsqlDbType.Uuid,
            StorageColumnType.Long => NpgsqlDbType.Bigint,
            StorageColumnType.Int => NpgsqlDbType.Integer,
            StorageColumnType.Boolean => NpgsqlDbType.Boolean,
            StorageColumnType.Timestamp => NpgsqlDbType.TimestampTz,
            StorageColumnType.Json => NpgsqlDbType.Jsonb,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    public void SetIdParameterType(DbParameter parameter, Type rawSqlType)
        => ((NpgsqlParameter)parameter).NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(rawSqlType);
}
