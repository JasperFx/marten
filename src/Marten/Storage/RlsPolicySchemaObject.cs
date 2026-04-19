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
    private readonly string? _settingName;
    private readonly bool _enabled;

    /// <summary>
    /// Create a new RLS policy schema object for the given document table.
    /// </summary>
    /// <param name="tableName">The document table to attach the policy to.</param>
    /// <param name="settingName">The PostgreSQL session setting name that holds the current tenant id. Null disables Marten-managed RLS for this table.</param>
    public RlsPolicySchemaObject(DbObjectName tableName, string? settingName)
    {
        _tableName = tableName;
        _settingName = settingName;
        _enabled = !string.IsNullOrWhiteSpace(settingName);
        Identifier = new PostgresqlObjectName(
            _tableName.Schema,
            PostgresqlIdentifier.Shorten($"{_tableName.Name}_{PolicyName}"),
            SchemaUtils.IdentifierUsage.General);
    }

    /// <inheritdoc />
    public DbObjectName Identifier { get; }

    /// <inheritdoc />
    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        var qualifiedTable = $"{_tableName.Schema}.{_tableName.Name}";
        if (_enabled)
        {
            writer.WriteLine($"ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;");
            writer.WriteLine($"ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY;");
            writer.WriteLine($"DROP POLICY IF EXISTS {PolicyName} ON {qualifiedTable};");
            writer.WriteLine($"CREATE POLICY {PolicyName} ON {qualifiedTable}");
            writer.WriteLine($"    USING (tenant_id = current_setting('{_settingName}'))");
            writer.WriteLine($"    WITH CHECK (tenant_id = current_setting('{_settingName}'));");
        }
        else
        {
            writer.WriteLine($"DROP POLICY IF EXISTS {PolicyName} ON {qualifiedTable};");
            writer.WriteLine($"ALTER TABLE {qualifiedTable} NO FORCE ROW LEVEL SECURITY;");
            writer.WriteLine($"ALTER TABLE {qualifiedTable} DISABLE ROW LEVEL SECURITY;");
        }

        writer.WriteLine();
    }

    /// <inheritdoc />
    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        var qualifiedTable = $"{_tableName.Schema}.{_tableName.Name}";
        writer.WriteLine($"DROP POLICY IF EXISTS {PolicyName} ON {qualifiedTable};");
        writer.WriteLine($"ALTER TABLE {qualifiedTable} NO FORCE ROW LEVEL SECURITY;");
        writer.WriteLine($"ALTER TABLE {qualifiedTable} DISABLE ROW LEVEL SECURITY;");
        writer.WriteLine();
    }

    /// <inheritdoc />
    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        // Weasel's AddParameter only binds the value — the caller is responsible
        // for writing the placeholder. AppendParameter does both, so we use it
        // rather than interpolating {param.ParameterName} (which emits "p0"
        // without the "@" prefix and is read by PG as a column identifier).
        if (_enabled)
        {
            // Match both RLS flags AND that the policy's USING / WITH CHECK
            // clauses reference current_setting('<settingName>'). If the user
            // reconfigures RLS to use a different setting name, the LIKE fails
            // and the Create path drops + recreates the policy.
            builder.Append("SELECT 1 FROM pg_class c JOIN pg_namespace n ON c.relnamespace = n.oid WHERE n.nspname = ");
            builder.AppendParameter(_tableName.Schema);
            builder.Append(" AND c.relname = ");
            builder.AppendParameter(_tableName.Name);
            builder.Append(" AND c.relrowsecurity = TRUE AND c.relforcerowsecurity = TRUE AND EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = ");
            builder.AppendParameter(PolicyName);
            builder.Append(" AND pg_get_expr(p.polqual, p.polrelid) LIKE ");
            var expressionPattern = $"%current_setting('{_settingName}'%";
            builder.AppendParameter(expressionPattern);
            builder.Append(" AND pg_get_expr(p.polwithcheck, p.polrelid) LIKE ");
            builder.AppendParameter(expressionPattern);
            builder.Append(");");
        }
        else
        {
            builder.Append("SELECT 1 FROM pg_class c JOIN pg_namespace n ON c.relnamespace = n.oid WHERE n.nspname = ");
            builder.AppendParameter(_tableName.Schema);
            builder.Append(" AND c.relname = ");
            builder.AppendParameter(_tableName.Name);
            builder.Append(" AND c.relrowsecurity = FALSE AND c.relforcerowsecurity = FALSE AND NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = ");
            builder.AppendParameter(PolicyName);
            builder.Append(");");
        }
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
