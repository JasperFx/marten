#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    /// <summary>
    ///     Fetch the next value of a PostgreSQL sequence by name (optionally schema-qualified).
    ///     The underlying call is the Postgres <c>nextval()</c> function. The returned value
    ///     is cast to <see cref="int" /> in SQL; use
    ///     <see cref="NextSequenceValueAsLong(string, CancellationToken)" /> when the sequence
    ///     may exceed <see cref="int.MaxValue" />.
    /// </summary>
    /// <param name="sequenceName">The sequence name. Schema-qualified names are supported.</param>
    /// <param name="token"></param>
    public async Task<int> NextSequenceValue(string sequenceName, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sequenceName);

        await using var cmd = new NpgsqlCommand("select nextval(:seq)::int");
        cmd.Parameters.Add(new NpgsqlParameter("seq", NpgsqlDbType.Text) { Value = sequenceName });

        await using var reader = await ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
        await reader.ReadAsync(token).ConfigureAwait(false);
        return await reader.GetFieldValueAsync<int>(0, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Fetch the next value of a PostgreSQL sequence described by a <see cref="DbObjectName" />.
    ///     The underlying call is the Postgres <c>nextval()</c> function. The returned value
    ///     is cast to <see cref="int" /> in SQL; use
    ///     <see cref="NextSequenceValueAsLong(DbObjectName, CancellationToken)" /> when the sequence
    ///     may exceed <see cref="int.MaxValue" />.
    /// </summary>
    /// <param name="sequenceName">The sequence identifier including its schema.</param>
    /// <param name="token"></param>
    public Task<int> NextSequenceValue(DbObjectName sequenceName, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sequenceName);
        return NextSequenceValue(sequenceName.QualifiedName, token);
    }

    /// <summary>
    ///     Fetch the next value of a PostgreSQL sequence by name (optionally schema-qualified)
    ///     as a 64-bit integer, matching the native type of <c>nextval()</c>.
    /// </summary>
    /// <param name="sequenceName">The sequence name. Schema-qualified names are supported.</param>
    /// <param name="token"></param>
    public async Task<long> NextSequenceValueAsLong(string sequenceName, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sequenceName);

        await using var cmd = new NpgsqlCommand("select nextval(:seq)");
        cmd.Parameters.Add(new NpgsqlParameter("seq", NpgsqlDbType.Text) { Value = sequenceName });

        await using var reader = await ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
        await reader.ReadAsync(token).ConfigureAwait(false);
        return await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Fetch the next value of a PostgreSQL sequence described by a <see cref="DbObjectName" />
    ///     as a 64-bit integer, matching the native type of <c>nextval()</c>.
    /// </summary>
    /// <param name="sequenceName">The sequence identifier including its schema.</param>
    /// <param name="token"></param>
    public Task<long> NextSequenceValueAsLong(DbObjectName sequenceName, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sequenceName);
        return NextSequenceValueAsLong(sequenceName.QualifiedName, token);
    }
}
