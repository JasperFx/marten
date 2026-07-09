# PostGIS Spatial Support

`Marten.PostGIS` is an optional companion package that adds spatial-data support to Marten on top of the [PostGIS](https://postgis.net/) PostgreSQL extension. It is published from the Marten repo under the MIT license and ships as the `Marten.PostGIS` NuGet package.

What it gives you:

- a one-line `UsePostGIS()` opt-in that registers the `postgis` extension on every database Marten manages (including per-tenant databases)
- Newtonsoft.Json converters that round-trip [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) geometry types (`Point`, `Polygon`, `LineString`, …) into the JSONB document
- four query helpers — `NearestToAsync`, `WithinDistanceAsync`, `ContainingAsync`, `IntersectingAsync` — that translate to the canonical PostGIS operators

## Installation

```shell
dotnet add package Marten.PostGIS
```

Your local PostgreSQL must ship the `postgis` extension. The Dockerfile under `docker/postgres/Dockerfile` in this repo layers `postgresql-17-postgis-3` (and `postgresql-17-pgvector`) on the official multi-arch `postgres:17` image.

## Enabling PostGIS on a store

```csharp
using Marten;
using Marten.PostGIS;

var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);

    // 1. Adds CREATE EXTENSION IF NOT EXISTS postgis to every database
    // 2. Calls NpgsqlDataSourceBuilder.UseNetTopologySuite() so NTS types
    //    round-trip through Npgsql
    // 3. Swaps in a JsonNetSerializer with the NTS GeoJsonSerializer
    //    converters registered, so NTS geometries serialize as GeoJSON
    //    inside the document's JSONB column
    opts.UsePostGIS();

    opts.RegisterDocumentType<StoreLocation>();
});
```

`UsePostGIS()` is multi-tenant aware. Multi-database setups (single-server-per-tenant, master-table tenancy, sharded tenancy) all create the extension in each tenant database via Marten's `ExtendedSchemaObjects`.

## Modelling a spatial document

Put any NetTopologySuite geometry type on your document. The default factory `new GeometryFactory(new PrecisionModel(), 4326)` corresponds to [WGS 84](https://en.wikipedia.org/wiki/World_Geodetic_System) — the standard lat/lon coordinate system.

```csharp
using NetTopologySuite.Geometries;

public class StoreLocation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Point? Location { get; set; }
}

public class ServiceArea
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Polygon? Boundary { get; set; }
}
```

Insert and load just like any Marten document:

```csharp
var wgs84 = new GeometryFactory(new PrecisionModel(), 4326);

await using (var session = store.LightweightSession())
{
    session.Store(new StoreLocation
    {
        Id = Guid.NewGuid(),
        Name = "Downtown Store",
        Location = wgs84.CreatePoint(new Coordinate(-122.33, 47.61))
    });
    await session.SaveChangesAsync();
}
```

## Spatial queries

The query helpers are extension methods on `IQuerySession`. They take a lambda picking the spatial property, an `NTS` geometry, and (for distance-flavoured queries) a `SpatialType`:

| `SpatialType`         | PostGIS cast   | When to use                                                                                    |
| --------------------- | -------------- | ---------------------------------------------------------------------------------------------- |
| `Geography` (default) | `::geography`  | Lat/lon on Earth — distances are in **metres**, accurate for global data                       |
| `Geometry`            | `::geometry`   | Cartesian (projected) plane — faster, distances are in the SRID's units (degrees for WGS 84)   |

### Nearest neighbor

```csharp
await using var q = store.QuerySession();

var nearest = await q.NearestToAsync<StoreLocation>(
    x => x.Location,
    point: wgs84.CreatePoint(new Coordinate(-122.33, 47.61)),
    limit: 5,
    spatialType: SpatialType.Geometry);
```

Translates to `ORDER BY <spatial>::<type> <-> $1 LIMIT $2` using the [`<->` KNN operator](https://postgis.net/docs/geometry_distance_knn.html), which is index-accelerated when a GiST index exists on the column.

### Within a distance

```csharp
var nearby = await q.WithinDistanceAsync<StoreLocation>(
    x => x.Location,
    point: downtownSeattle,
    distanceMeters: 5000,
    spatialType: SpatialType.Geography);
```

Translates to `ST_DWithin(<spatial>::<type>, $1, $2)` — the canonical index-accelerated distance filter.

### Containing / intersecting

```csharp
var coveringAreas = await q.ContainingAsync<ServiceArea>(
    x => x.Boundary, downtownSeattle, SpatialType.Geometry);

var overlappingAreas = await q.IntersectingAsync<ServiceArea>(
    x => x.Boundary, marketBoundary, SpatialType.Geometry);
```

These map to [`ST_Contains`](https://postgis.net/docs/ST_Contains.html) and [`ST_Intersects`](https://postgis.net/docs/ST_Intersects.html) respectively.

## Notes & limitations

- The query helpers run raw SQL through the session's connection — they do not go through Marten's LINQ provider or compiled-query cache. Document instances are deserialized via the store's `ISerializer`.
- The spatial value lives inside the JSONB document and is cast to PostGIS types at query time (`ST_GeomFromGeoJSON(d.data->'<member>')::<type>`). For large tables, add a [functional GiST index](https://postgis.net/workshops/postgis-intro/indexing.html) on that expression to keep the spatial operators index-accelerated.
- The Newtonsoft `JsonNetSerializer` is registered for you by `UsePostGIS()`. If you have your own serializer configuration, call `UsePostGIS()` first and tweak the serializer afterwards.
- Only simple member access expressions are supported in the spatial property selector (`x => x.Location`), matching the Marten LINQ conventions.
