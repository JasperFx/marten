#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Weasel.Core;
using Weasel.Postgresql;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;
using DbDataReader = System.Data.Common.DbDataReader;

namespace Marten.Storage;

/// <summary>
/// Schema object that manages the PostgreSQL Row Level Security policy
/// enforcing tenant isolation on a conjoined-tenancy document table.
/// </summary>
internal class RlsPolicySchemaObject: ISchemaObject
{
    private const string PolicyName = "marten_tenant_isolation";
    private readonly DbObjectName _tableName;
    private readonly string _settingName;

    /// <summary>
    /// Create a new RLS policy schema object for the given document table.
    /// </summary>
    /// <param name="tableName">The document table to attach the policy to.</param>
    /// <param name="settingName">The PostgreSQL session setting name that holds the current tenant id.</param>
    public RlsPolicySchemaObject(DbObjectName tableName, string settingName)
    {
        _tableName = tableName;
        _settingName = settingName;
        Identifier = new PostgresqlObjectName(_tableName.Schema, $"{_tableName.Name}_{PolicyName}");
    }

    /// <inheritdoc />
    public DbObjectName Identifier { get; }

    /// <inheritdoc />
    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        var qualifiedTable = $"{_tableName.Schema}.{_tableName.Name}";
        writer.WriteLine($"ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;");
        writer.WriteLine($"ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY;");
        writer.WriteLine($"CREATE POLICY {PolicyName} ON {qualifiedTable}");
        writer.WriteLine($"    USING (tenant_id = current_setting('{_settingName}'))");
        writer.WriteLine($"    WITH CHECK (tenant_id = current_setting('{_settingName}'));");
        writer.WriteLine();
    }

    /// <inheritdoc />
    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        var qualifiedTable = $"{_tableName.Schema}.{_tableName.Name}";
        writer.WriteLine($"DROP POLICY IF EXISTS {PolicyName} ON {qualifiedTable};");
        writer.WriteLine($"ALTER TABLE {qualifiedTable} DISABLE ROW LEVEL SECURITY;");
        writer.WriteLine();
    }

    /// <inheritdoc />
    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        var schemaParam = builder.AddParameter(_tableName.Schema);
        var tableParam = builder.AddParameter(_tableName.Name);
        builder.Append(
            $"SELECT polname FROM pg_policy p JOIN pg_class c ON p.polrelid = c.oid JOIN pg_namespace n ON c.relnamespace = n.oid WHERE n.nspname = {schemaParam.ParameterName} AND c.relname = {tableParam.ParameterName} AND p.polname = '{PolicyName}'");
    }

    /// <inheritdoc />
    public async Task<ISchemaObjectDelta> CreateDeltaAsync(DbDataReader reader, CancellationToken ct = default)
    {
        var exists = await reader.ReadAsync(ct).ConfigureAwait(false);
        return new SchemaObjectDelta(this, exists ? SchemaPatchDifference.None : SchemaPatchDifference.Create);
    }

    /// <inheritdoc />
    public IEnumerable<DbObjectName> AllNames()
    {
        yield return Identifier;
    }
}
