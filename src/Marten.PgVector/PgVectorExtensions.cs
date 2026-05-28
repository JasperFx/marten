using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Internal.Sessions;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using Weasel.Postgresql;

namespace Marten.PgVector;

public static class PgVectorExtensions
{
    /// <summary>
    /// Enable pgvector support for this Marten store. This registers the PostgreSQL
    /// "vector" extension and configures Npgsql to handle vector types on all
    /// data sources, including tenant databases.
    /// </summary>
    public static StoreOptions UsePgVector(this StoreOptions opts)
    {
        // Configure all NpgsqlDataSourceBuilders to support pgvector types
        opts.ConfigureNpgsqlDataSourceBuilder(b => b.UseVector());

        // Register the PostgreSQL "vector" extension for schema management.
        // This ensures CREATE EXTENSION IF NOT EXISTS vector runs on every database.
        opts.Storage.ExtendedSchemaObjects.Add(new Extension("vector"));

        return opts;
    }

    /// <summary>
    /// Search for documents by vector similarity using a dedicated vector column.
    /// The vector data is stored as a float array in the JSONB document and queried
    /// via a cast to the vector type.
    /// </summary>
    public static async Task<IReadOnlyList<T>> VectorSearchAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> vectorProperty,
        Vector queryVector,
        int limit = 10,
        DistanceFunction distance = DistanceFunction.Cosine) where T : class
    {
        var store = (DocumentStore)session.DocumentStore;
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();

        // Build a JSONB path to the vector property.
        // Use the serializer to determine the correct JSON property name.
        var member = GetMemberInfo(vectorProperty);
        var jsonPath = member.Name;

        var op = distance.Operator();
        var dimensions = queryVector.ToArray().Length;

        // Build WHERE clause with optional tenant filtering for conjoined tenancy
        var whereClause = $"d.data->>'{jsonPath}' IS NOT NULL";
        // Apply tenant_id filtering only for conjoined tenancy (shared table with tenant_id column).
        // For database-per-tenant, isolation is handled by connecting to the tenant's database.
        var tenantId = session.TenantId;
        var isSingleDatabase = store.Options.Tenancy.Cardinality == JasperFx.Descriptors.DatabaseCardinality.Single;
        var hasTenantFilter = isSingleDatabase
            && !string.IsNullOrEmpty(tenantId)
            && tenantId != JasperFx.StorageConstants.DefaultTenantId;

        if (hasTenantFilter)
        {
            whereClause += " AND d.tenant_id = $3";
        }

        var sql = $"select d.data from {tableName} d " +
                  $"WHERE {whereClause} " +
                  $"ORDER BY (d.data->>'{jsonPath}')::vector({dimensions}) {op} $1 LIMIT $2";

        var results = new List<T>();

        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = queryVector });
        cmd.Parameters.Add(new NpgsqlParameter { Value = limit });
        if (hasTenantFilter)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = tenantId });
        }

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var serializer = store.Serializer;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var json = await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var doc = serializer.FromJson<T>(new MemoryStream(bytes));
            if (doc != null) results.Add(doc);
        }

        return results;
    }

    private static MemberInfo GetMemberInfo<T>(Expression<Func<T, object?>> expression)
    {
        var body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        return body switch
        {
            MemberExpression memberExpr => memberExpr.Member,
            _ => throw new ArgumentException("Expression must be a simple property or field access")
        };
    }
}
