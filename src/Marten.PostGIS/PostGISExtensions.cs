using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using JasperFx.Core.Reflection;
using Marten.Internal.Sessions;
using NetTopologySuite.Geometries;
using Npgsql;
using Npgsql.NetTopologySuite;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.PostGIS;

public static class PostGISExtensions
{
    /// <summary>
    /// Enable PostGIS support for this Marten store. This registers the PostgreSQL
    /// "postgis" extension and configures Npgsql to handle NTS spatial types on all
    /// data sources, including tenant databases.
    /// </summary>
    public static StoreOptions UsePostGIS(this StoreOptions opts)
    {
        opts.ConfigureNpgsqlDataSourceBuilder(b => b.UseNetTopologySuite());
        opts.Storage.ExtendedSchemaObjects.Add(new Extension("postgis"));

        // Register NTS GeoJSON converters with the Newtonsoft.Json serializer
        // so that NTS types (Point, Polygon, etc.) are properly serialized/deserialized
        // in Marten's JSONB document storage
        var serializer = new Marten.Services.JsonNetSerializer();
        serializer.Configure(s =>
        {
            var geoJsonSerializer = NetTopologySuite.IO.GeoJsonSerializer.Create(
                s, new GeometryFactory(new PrecisionModel(), 4326));
            foreach (var converter in geoJsonSerializer.Converters)
            {
                s.Converters.Add(converter);
            }
        });
        opts.Serializer(serializer);

        return opts;
    }

    /// <summary>
    /// Find the nearest documents to a point, ordered by distance.
    /// Uses PostGIS ST_Distance for ordering.
    /// </summary>
    public static async Task<IReadOnlyList<T>> NearestToAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> spatialProperty,
        Point point,
        int limit = 10,
        SpatialType spatialType = SpatialType.Geography) where T : class
    {
        var store = (DocumentStore)session.DocumentStore;
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();
        var member = GetMemberInfo(spatialProperty);
        var jsonPath = member.Name;
        var pgType = spatialType == SpatialType.Geography ? "geography" : "geometry";

        // Use the <-> KNN operator for index-accelerated nearest neighbor
        var sql = $"SELECT d.data FROM {tableName} d " +
                  $"WHERE d.data->>'{jsonPath}' IS NOT NULL " +
                  $"ORDER BY ST_GeomFromGeoJSON(d.data->'{jsonPath}')::{ pgType} <-> $1 " +
                  $"LIMIT $2";

        return await ExecuteSpatialQuery<T>(session, store, sql, point, limit);
    }

    /// <summary>
    /// Find all documents whose spatial property is within a given distance of a point.
    /// Uses PostGIS ST_DWithin for index-accelerated distance filtering.
    /// </summary>
    public static async Task<IReadOnlyList<T>> WithinDistanceAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> spatialProperty,
        Point point,
        double distanceMeters,
        SpatialType spatialType = SpatialType.Geography) where T : class
    {
        var store = (DocumentStore)session.DocumentStore;
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();
        var member = GetMemberInfo(spatialProperty);
        var jsonPath = member.Name;
        var pgType = spatialType == SpatialType.Geography ? "geography" : "geometry";

        var sql = $"SELECT d.data FROM {tableName} d " +
                  $"WHERE ST_DWithin(" +
                  $"ST_GeomFromGeoJSON(d.data->'{jsonPath}')::{pgType}, " +
                  $"$1, $2)";

        var results = new List<T>();
        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = point, NpgsqlDbType = NpgsqlDbType.Geometry });
        cmd.Parameters.Add(new NpgsqlParameter { Value = distanceMeters });

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var serializer = store.Serializer;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var json = await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
            var doc = serializer.FromJson<T>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            if (doc != null) results.Add(doc);
        }

        return results;
    }

    /// <summary>
    /// Find all documents whose spatial property contains the given geometry.
    /// Uses PostGIS ST_Contains.
    /// </summary>
    public static async Task<IReadOnlyList<T>> ContainingAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> spatialProperty,
        Geometry geometry,
        SpatialType spatialType = SpatialType.Geography) where T : class
    {
        var store = (DocumentStore)session.DocumentStore;
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();
        var member = GetMemberInfo(spatialProperty);
        var jsonPath = member.Name;
        var pgType = spatialType == SpatialType.Geography ? "geography" : "geometry";

        var sql = $"SELECT d.data FROM {tableName} d " +
                  $"WHERE ST_Contains(" +
                  $"ST_GeomFromGeoJSON(d.data->'{jsonPath}')::{pgType}, " +
                  $"$1)";

        var results = new List<T>();
        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = geometry, NpgsqlDbType = NpgsqlDbType.Geometry });

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var serializer = store.Serializer;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var json = await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
            var doc = serializer.FromJson<T>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            if (doc != null) results.Add(doc);
        }

        return results;
    }

    /// <summary>
    /// Find all documents whose spatial property intersects the given geometry.
    /// Uses PostGIS ST_Intersects.
    /// </summary>
    public static async Task<IReadOnlyList<T>> IntersectingAsync<T>(
        this IQuerySession session,
        Expression<Func<T, object?>> spatialProperty,
        Geometry geometry,
        SpatialType spatialType = SpatialType.Geography) where T : class
    {
        var store = (DocumentStore)session.DocumentStore;
        var tableName = ((IReadOnlyStoreOptions)store.Options).Schema.For<T>();
        var member = GetMemberInfo(spatialProperty);
        var jsonPath = member.Name;
        var pgType = spatialType == SpatialType.Geography ? "geography" : "geometry";

        var sql = $"SELECT d.data FROM {tableName} d " +
                  $"WHERE ST_Intersects(" +
                  $"ST_GeomFromGeoJSON(d.data->'{jsonPath}')::{pgType}, " +
                  $"$1)";

        var results = new List<T>();
        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = geometry, NpgsqlDbType = NpgsqlDbType.Geometry });

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var serializer = store.Serializer;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var json = await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
            var doc = serializer.FromJson<T>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            if (doc != null) results.Add(doc);
        }

        return results;
    }

    #region Private helpers

    private static async Task<IReadOnlyList<T>> ExecuteSpatialQuery<T>(
        IQuerySession session, DocumentStore store, string sql,
        Point point, int limit) where T : class
    {
        var results = new List<T>();
        var database = session.As<QuerySession>().Database;
        await using var conn = database.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = point, NpgsqlDbType = NpgsqlDbType.Geometry });
        cmd.Parameters.Add(new NpgsqlParameter { Value = limit });

        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var serializer = store.Serializer;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var json = await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
            var doc = serializer.FromJson<T>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
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

    #endregion
}
