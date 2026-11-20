#nullable enable
using System;
using System.Globalization;
using System.Text;
using JasperFx.Core.Reflection;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
/// marten#6003: the slice index of one multi-stream projection version (<c>UseSliceIndex()</c>): for every event the
/// daemon applied, which identities it went to. One table per projection name and version, keyed by
/// (tenant, identity, sequence), with the identity in its native column type.
/// </summary>
/// <remarks>
/// Created only for a projection that opts in. The daemon writes it in the batch that applies the events, prunes it on
/// the shard's heartbeat under <c>SliceIndexRetention.ForParking</c>, and rebuild teardown, a rewind, a tenant's removal
/// and a retired version's eject delete its rows. No foreign key to <c>mt_events</c>: compacted or tombstoned events
/// leave rows the loader ignores, which is cheaper than checking a key on every delete of an event.
/// </remarks>
internal class SliceIndexTable: Table
{
    public const string Prefix = "mt_slice_index_";

    public SliceIndexTable(EventGraph eventGraph, string projectionName, uint version, Type identityType)
        : base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, NameFor(projectionName, version)))
    {
        foreach (var index in eventGraph.IgnoredIndexes)
            IgnoredIndexes.Add(index);

        AddColumn("tenant_id", "varchar").NotNull().AsPrimaryKey();
        AddColumn("identity", PostgresqlTypeFor(identityType)).NotNull().AsPrimaryKey();
        AddColumn("seq_id", "bigint").NotNull().AsPrimaryKey();

        PrimaryKeyName = PostgresqlIdentifier.Shorten("pk_" + Identifier.Name);

        // Pruning and a rewind delete by sequence
        Indexes.Add(new IndexDefinition(PostgresqlIdentifier.Shorten($"idx_{Identifier.Name}_seq_id"))
        {
            Columns = ["seq_id"]
        });
    }

    /// <summary>
    /// The table name of one projection version's slice index: <c>mt_slice_index_{name}_v{version}</c>, with the
    /// name lower-cased, anything that isn't a letter, digit or underscore replaced by an underscore, and shortened to
    /// fit PostgreSQL's identifier length.
    /// </summary>
    public static string NameFor(string projectionName, uint version)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectionName);

        var builder = new StringBuilder(Prefix);
        foreach (var c in projectionName.ToLowerInvariant())
        {
            builder.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_');
        }

        builder.Append("_v");
        builder.Append(version.ToString(CultureInfo.InvariantCulture));

        return PostgresqlIdentifier.Shorten(builder.ToString());
    }

    /// <summary>The simple type an identity of <paramref name="identityType" /> is stored as.</summary>
    public static Type SimpleTypeOf(Type identityType)
    {
        var target = Nullable.GetUnderlyingType(identityType) ?? identityType;
        if (target == typeof(Guid) || target == typeof(string) || target == typeof(int) || target == typeof(long))
        {
            return target;
        }

        if (StrongTypedIdResolver.TryResolve(target, out var info, StrongTypedIdStyle.AnyType))
        {
            return info.SimpleType;
        }

        throw new ArgumentOutOfRangeException(nameof(identityType),
            $"A slice index can't hold identities of type {identityType.FullNameInCode()}. Use a Guid, string, int or long identity, or a strong-typed identifier wrapping one.");
    }

    internal static string PostgresqlTypeFor(Type identityType)
    {
        var simple = SimpleTypeOf(identityType);
        if (simple == typeof(Guid)) return "uuid";
        if (simple == typeof(int)) return "integer";
        if (simple == typeof(long)) return "bigint";
        return "varchar";
    }
}
