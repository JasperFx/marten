#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Documents;
using Marten.Storage;
using Npgsql;

namespace Marten;

public partial class DocumentStore
{
    /// <summary>
    /// jasperfx#1078: how PostgreSQL executes the page query <c>QueryDocumentsAsync</c> would run for the same
    /// arguments. Built by the same body as the read (<see cref="runDocumentQueryAsync" />), so the explained
    /// statement is the read's own -- Where, OrderBy, OrderByMetadata, the metadata filters, IdEquals, tenancy,
    /// AllTenants, soft deletes and paging included -- and every refusal is the read's, with the same exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unknown type name is <see langword="null" />: there is no query to explain. A request the read
    /// answers WITHOUT touching the database -- an id that is not a value of the identity type, a tenant this
    /// store does not know -- is a plan saying so, with zero rows read and returned, rather than null, because
    /// the store does know exactly what that read costs.
    /// </para>
    /// <para>
    /// <c>EXPLAIN ANALYZE</c> executes the statement, so it runs in a <c>READ ONLY</c> transaction that is then
    /// rolled back. The page statement is a <c>SELECT</c> either way; the transaction makes "nothing is written"
    /// a property of the database session rather than of the SQL.
    /// </para>
    /// </remarks>
    async Task<DocumentQueryPlan?> IDocumentStoreDiagnostics.ExplainDocumentQueryAsync(
        string documentTypeName, DocumentQueryOptions options, CancellationToken token)
        => (await runDocumentQueryAsync(documentTypeName, options, explain: true, token).ConfigureAwait(false))
            .Plan;

    /// <summary>
    /// What <see cref="runDocumentQueryAsync" /> answered: a page for a read, a plan for an explain.
    /// </summary>
    internal readonly record struct DocumentQueryOutcome(DocumentQueryResult? Result, DocumentQueryPlan? Plan)
    {
        /// <summary>
        /// A request answered without reading anything: an empty page to a read, and to an explain a plan that
        /// says no statement ran.
        /// </summary>
        public static DocumentQueryOutcome Empty(int pageNumber, int pageSize, string reason)
            => new(emptyPage(pageNumber, pageSize),
                new DocumentQueryPlan(PostgresqlEngine, $"No query was executed: {reason}.")
                {
                    RowsExamined = 0,
                    RowsReturned = 0,
                    IndexesUsed = Array.Empty<string>(),
                    ScansTable = false,
                    IsActual = true
                });
    }

    internal const string PostgresqlEngine = "PostgreSQL";

    /// <summary>
    /// Runs <paramref name="page" /> under <c>EXPLAIN (ANALYZE, FORMAT JSON)</c> in a read-only transaction that
    /// is rolled back, and reads the plan.
    /// </summary>
    /// <param name="rlsTenantId">
    /// The tenant the read's own session would have set for row-level security, or null for the raw read, which
    /// runs on a bare connection.
    /// </param>
    private async Task<DocumentQueryPlan> explainPageAsync(IMartenDatabase database, NpgsqlCommand page,
        DiagnosticsTarget target, string? rlsTenantId, CancellationToken token)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false);

        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
        {
            await readOnly.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        if (rlsTenantId != null && Options.RlsTenantSessionSetting is { } rls)
        {
            // Transaction-local (is_local = true), so it ends with the rollback below.
            await using var setTenant = new NpgsqlCommand("SELECT set_config(@name, @value, true)", conn, tx);
            setTenant.Parameters.AddWithValue("name", rls);
            setTenant.Parameters.AddWithValue("value", rlsTenantId);
            await setTenant.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        page.Connection = conn;
        page.Transaction = tx;
        page.CommandText = "EXPLAIN (ANALYZE, FORMAT JSON) " + page.CommandText;

        var json = Convert.ToString(await page.ExecuteScalarAsync(token).ConfigureAwait(false))!;

        await tx.RollbackAsync(token).ConfigureAwait(false);

        return PostgresqlPlanReader.Read(json, target.Mapping.TableName.Name, target.Mapping.Partitioning != null);
    }

    /// <summary>
    /// jasperfx#1078: reads PostgreSQL's <c>EXPLAIN (ANALYZE, FORMAT JSON)</c> output into a
    /// <see cref="DocumentQueryPlan" />.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>RowsReturned</b> is the top node's actual rows.</item>
    /// <item><b>RowsExamined</b> is what the table-reading nodes (those with a <c>Relation Name</c>: Seq Scan,
    /// Index Scan, Index Only Scan, Bitmap Heap Scan, ...) read: their actual rows plus the rows they removed by
    /// filter and by index recheck, times their loops. PostgreSQL prints both counts per loop. A Bitmap Index
    /// Scan names no relation, so its rows are not counted twice with the heap scan above it.</item>
    /// <item><b>IndexesUsed</b> is every distinct <c>Index Name</c> in the tree.</item>
    /// <item><b>ScansTable</b> is whether the document table (or, for a partitioned table, one of its partitions)
    /// is read row by row: a Seq Scan, or an Index Scan / Index Only Scan with no <c>Index Cond</c>. The second is
    /// what PostgreSQL usually picks for an unindexed filter under the page's <c>ORDER BY id LIMIT</c> -- it walks
    /// the whole primary key for the order and filters every row -- and it answers "did this filter scan?" with a
    /// yes as surely as a Seq Scan, which is why the primary key can appear in <c>IndexesUsed</c> beside it.</item>
    /// </list>
    /// <para>
    /// <see cref="DocumentQueryPlan.PlanText" /> is the JSON plan as PostgreSQL printed it. Running the
    /// statement a second time for the text format would double the cost of the explain and could disagree
    /// with the numbers read here.
    /// </para>
    /// </remarks>
    internal static class PostgresqlPlanReader
    {
        public static DocumentQueryPlan Read(string json, string tableName, bool partitioned)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                root = root[0];
            }

            var top = root.GetProperty("Plan");

            long examined = 0;
            var indexes = new List<string>();
            var scansTable = false;

            visit(top);

            return new DocumentQueryPlan(PostgresqlEngine, json)
            {
                RowsReturned = (long)Math.Round(number(top, "Actual Rows") * Math.Max(1, number(top, "Actual Loops"))),
                RowsExamined = examined,
                IndexesUsed = indexes,
                ScansTable = scansTable,
                IsActual = top.TryGetProperty("Actual Rows", out _)
            };

            void visit(JsonElement node)
            {
                var nodeType = node.TryGetProperty("Node Type", out var t) ? t.GetString() : null;

                if (node.TryGetProperty("Relation Name", out var relation))
                {
                    var loops = Math.Max(1, number(node, "Actual Loops"));
                    var perLoop = number(node, "Actual Rows") + number(node, "Rows Removed by Filter")
                                  + number(node, "Rows Removed by Index Recheck");
                    examined += (long)Math.Round(perLoop * loops);

                    if (readsEveryRow(node, nodeType) && isDocumentTable(relation.GetString()))
                    {
                        scansTable = true;
                    }
                }

                if (node.TryGetProperty("Index Name", out var index) && index.GetString() is { } name &&
                    !indexes.Contains(name))
                {
                    indexes.Add(name);
                }

                if (node.TryGetProperty("Plans", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                    {
                        visit(child);
                    }
                }
            }

            // A Seq Scan, or an index scan with no Index Cond: PostgreSQL walking the whole primary key for its
            // ORDER BY id and filtering every row is the usual shape of an unindexed filter under a LIMIT, and it
            // reads the whole table as surely as a Seq Scan does.
            static bool readsEveryRow(JsonElement node, string? nodeType)
                => nodeType == "Seq Scan"
                   || (nodeType is "Index Scan" or "Index Only Scan" && !node.TryGetProperty("Index Cond", out _));

            bool isDocumentTable(string? relationName)
                => relationName != null
                   && (string.Equals(relationName, tableName, StringComparison.OrdinalIgnoreCase)
                       || (partitioned && relationName.StartsWith(tableName + "_", StringComparison.OrdinalIgnoreCase)));
        }

        // PostgreSQL 18 prints actual rows with two decimals; earlier versions print integers.
        private static double number(JsonElement node, string property)
            => node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : 0;
    }
}
