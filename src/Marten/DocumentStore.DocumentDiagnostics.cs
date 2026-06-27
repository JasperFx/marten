#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Documents;
using JasperFx.MultiTenancy;
using Marten.Schema;
using Marten.Storage.Metadata;

namespace Marten;

public partial class DocumentStore : IDocumentStoreDiagnostics
{
    /// <summary>
    /// Store-agnostic, read-only document-query surface for monitoring consoles
    /// (CritterWatch #545). Mirrors the role <see cref="JasperFx.Events.IEventStore"/>
    /// plays for event streams: list the mapped document types, page their stored
    /// rows as raw JSON, and fetch one by id — all without the console referencing
    /// Marten directly. Reads the canonical <c>data</c> jsonb column straight off
    /// the document table so the JSON matches exactly what Marten persisted.
    /// </summary>
    async Task<IReadOnlyList<DocumentTypeRef>> IDocumentStoreDiagnostics.DocumentTypesAsync(
        CancellationToken token)
    {
        // Match TryCreateUsage: force lazy mappings to materialize before enumerating.
        Options.Storage.BuildAllMappings();

        var refs = Options.Storage.DocumentMappingsWithSchema
            .OrderBy(x => x.Alias)
            .Select(m => new DocumentTypeRef(m.DocumentType.FullNameInCode(), m.Alias, m.DatabaseSchemaName))
            .ToList();

        return await Task.FromResult<IReadOnlyList<DocumentTypeRef>>(refs).ConfigureAwait(false);
    }

    async Task<DocumentQueryResult> IDocumentStoreDiagnostics.QueryDocumentsAsync(
        string documentTypeName, DocumentQueryOptions options, CancellationToken token)
    {
        var mapping = ResolveMappingForDiagnostics(documentTypeName);
        if (mapping == null)
        {
            return new DocumentQueryResult(Array.Empty<string>(), 0, options.PageNumber, options.PageSize);
        }

        var table = mapping.TableName.QualifiedName;

        var pageNumber = Math.Max(1, options.PageNumber);
        var pageSize = Math.Max(1, options.PageSize);
        var offset = (pageNumber - 1) * pageSize;

        // Tenant scoping (#475 / EVENT_STORE_EXPLORER_PLAN §3.1): a database-per-tenant
        // store selects the tenant's physical database; conjoined tenancy keeps every
        // tenant in one database with a tenant_id column we filter on.
        var database = options.TenantId != null && Tenancy.Cardinality != DatabaseCardinality.Single
            ? await Tenancy.FindOrCreateDatabase(options.TenantId).ConfigureAwait(false)
            : Tenancy.Default.Database;
        var filterByTenant = options.TenantId != null
            && Tenancy.Cardinality == DatabaseCardinality.Single
            && mapping.TenancyStyle == TenancyStyle.Conjoined;

        var conditions = new List<string>();
        if (options.IdEquals != null) conditions.Add("id::text = @id");
        if (filterByTenant) conditions.Add("tenant_id = @tenant");

        // #4791 / CritterWatch #629: exact-match filters on the document's metadata columns
        // (correlation_id, causation_id, last_modified_by). A filter is honored only when both
        // the option is set AND the document mapping has the corresponding metadata column
        // enabled — emitting a WHERE on a column the table doesn't have would throw
        // 42703 (undefined_column). When the option is set but the column is disabled we
        // silently skip the filter (per the JasperFx DocumentQueryOptions contract: "Only honored
        // when the store advertises and captures the metadata column; otherwise ignored").
        var filterByCorrelationId = options.CorrelationId != null && mapping.Metadata.CorrelationId.Enabled;
        var filterByCausationId = options.CausationId != null && mapping.Metadata.CausationId.Enabled;
        var filterByLastModifiedBy = options.LastModifiedBy != null && mapping.Metadata.LastModifiedBy.Enabled;
        if (filterByCorrelationId) conditions.Add($"{CorrelationIdColumn.ColumnName} = @corr");
        if (filterByCausationId) conditions.Add($"{CausationIdColumn.ColumnName} = @caus");
        if (filterByLastModifiedBy) conditions.Add($"{LastModifiedByColumn.ColumnName} = @lmb");

        var where = conditions.Count > 0 ? " where " + string.Join(" and ", conditions) : "";

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        long total;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"select count(*) from {table}{where}";
            bindFilterParameters(countCmd);

            total = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));
        }

        var rows = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            // data::text guarantees the jsonb column comes back as JSON text.
            cmd.CommandText = $"select data::text from {table}{where} order by id limit {pageSize} offset {offset}";
            bindFilterParameters(cmd);

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(reader.GetString(0));
            }
        }

        return new DocumentQueryResult(rows, total, pageNumber, pageSize);

        void bindFilterParameters(Npgsql.NpgsqlCommand cmd)
        {
            if (options.IdEquals != null) cmd.Parameters.AddWithValue("id", options.IdEquals);
            if (filterByTenant) cmd.Parameters.AddWithValue("tenant", options.TenantId!);
            if (filterByCorrelationId) cmd.Parameters.AddWithValue("corr", options.CorrelationId!);
            if (filterByCausationId) cmd.Parameters.AddWithValue("caus", options.CausationId!);
            if (filterByLastModifiedBy) cmd.Parameters.AddWithValue("lmb", options.LastModifiedBy!);
        }
    }

    async Task<string?> IDocumentStoreDiagnostics.LoadDocumentJsonAsync(
        string documentTypeName, string id, CancellationToken token)
    {
        var mapping = ResolveMappingForDiagnostics(documentTypeName);
        if (mapping == null)
        {
            return null;
        }

        var table = mapping.TableName.QualifiedName;

        await using var conn = Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select data::text from {table} where id::text = @id limit 1";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
        return result == null || result is DBNull ? null : (string)result;
    }

    private DocumentMapping? ResolveMappingForDiagnostics(string documentTypeName)
    {
        Options.Storage.BuildAllMappings();

        return Options.Storage.DocumentMappingsWithSchema.FirstOrDefault(m =>
            string.Equals(m.DocumentType.FullNameInCode(), documentTypeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.DocumentType.FullName, documentTypeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.DocumentType.Name, documentTypeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.Alias, documentTypeName, StringComparison.OrdinalIgnoreCase));
    }
}
