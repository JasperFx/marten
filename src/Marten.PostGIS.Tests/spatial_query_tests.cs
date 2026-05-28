using Marten.PostGIS;
using Marten.Testing.Harness;
using NetTopologySuite.Geometries;
using Shouldly;
using Xunit;

namespace Marten.PostGIS.Tests;

public class spatial_query_tests : IAsyncLifetime
{
    private DocumentStore _store = null!;
    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "postgis_tests";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            opts.UsePostGIS();
            opts.RegisterDocumentType<StoreLocation>();
            opts.RegisterDocumentType<ServiceArea>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task postgis_extension_is_created()
    {
        await using var conn = _store.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM pg_extension WHERE extname = 'postgis'";
        var result = await cmd.ExecuteScalarAsync();
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task can_store_and_load_document_with_point()
    {
        var store = new StoreLocation
        {
            Id = Guid.NewGuid(),
            Name = "Downtown Store",
            Location = Wgs84.CreatePoint(new Coordinate(-122.33, 47.61))
        };

        await using (var session = _store.LightweightSession())
        {
            session.Store(store);
            await session.SaveChangesAsync();
        }

        await using (var q = _store.QuerySession())
        {
            var loaded = await q.LoadAsync<StoreLocation>(store.Id);
            loaded.ShouldNotBeNull();
            loaded.Name.ShouldBe("Downtown Store");
            // NTS Point is serialized as GeoJSON in the JSONB data column
        }
    }

    [Fact]
    public async Task can_store_and_load_document_with_polygon()
    {
        var area = new ServiceArea
        {
            Id = Guid.NewGuid(),
            Name = "Metro Area",
            Boundary = Wgs84.CreatePolygon(new[]
            {
                new Coordinate(-122.5, 47.5),
                new Coordinate(-122.0, 47.5),
                new Coordinate(-122.0, 47.8),
                new Coordinate(-122.5, 47.8),
                new Coordinate(-122.5, 47.5) // close the ring
            })
        };

        await using (var session = _store.LightweightSession())
        {
            session.Store(area);
            await session.SaveChangesAsync();
        }

        await using (var q = _store.QuerySession())
        {
            var loaded = await q.LoadAsync<ServiceArea>(area.Id);
            loaded.ShouldNotBeNull();
            loaded.Name.ShouldBe("Metro Area");
        }
    }

    [Fact]
    public async Task nearest_to_returns_ordered_by_distance()
    {
        // Seattle area stores
        var stores = new[]
        {
            new StoreLocation { Id = Guid.NewGuid(), Name = "Capitol Hill",
                Location = Wgs84.CreatePoint(new Coordinate(-122.32, 47.63)) },
            new StoreLocation { Id = Guid.NewGuid(), Name = "Bellevue",
                Location = Wgs84.CreatePoint(new Coordinate(-122.20, 47.61)) },
            new StoreLocation { Id = Guid.NewGuid(), Name = "Tacoma",
                Location = Wgs84.CreatePoint(new Coordinate(-122.44, 47.25)) },
        };

        await using (var session = _store.LightweightSession())
        {
            foreach (var s in stores) session.Store(s);
            await session.SaveChangesAsync();
        }

        // Search from downtown Seattle
        var downtown = Wgs84.CreatePoint(new Coordinate(-122.33, 47.61));

        await using var q = _store.QuerySession();
        var results = await q.NearestToAsync<StoreLocation>(
            x => x.Location, downtown, limit: 3, spatialType: SpatialType.Geometry);

        results.Count.ShouldBe(3);
        results[0].Name.ShouldBe("Capitol Hill"); // closest
        results[2].Name.ShouldBe("Tacoma"); // farthest
    }

    [Fact]
    public async Task within_distance_filters_correctly()
    {
        var stores = new[]
        {
            new StoreLocation { Id = Guid.NewGuid(), Name = "Nearby",
                Location = Wgs84.CreatePoint(new Coordinate(-122.33, 47.62)) },
            new StoreLocation { Id = Guid.NewGuid(), Name = "Far Away",
                Location = Wgs84.CreatePoint(new Coordinate(-120.0, 45.0)) },
        };

        await using (var session = _store.LightweightSession())
        {
            foreach (var s in stores) session.Store(s);
            await session.SaveChangesAsync();
        }

        // Search within ~50km of downtown Seattle using geometry (degrees)
        var downtown = Wgs84.CreatePoint(new Coordinate(-122.33, 47.61));

        await using var q = _store.QuerySession();
        var results = await q.WithinDistanceAsync<StoreLocation>(
            x => x.Location, downtown, distanceMeters: 0.5, // ~50km in degrees
            spatialType: SpatialType.Geometry);

        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("Nearby");
    }

    [Fact]
    public async Task containing_finds_polygons_that_contain_a_point()
    {
        var areas = new[]
        {
            new ServiceArea
            {
                Id = Guid.NewGuid(), Name = "Seattle Metro",
                Boundary = Wgs84.CreatePolygon(new[]
                {
                    new Coordinate(-122.5, 47.4),
                    new Coordinate(-122.0, 47.4),
                    new Coordinate(-122.0, 47.8),
                    new Coordinate(-122.5, 47.8),
                    new Coordinate(-122.5, 47.4)
                })
            },
            new ServiceArea
            {
                Id = Guid.NewGuid(), Name = "Portland Metro",
                Boundary = Wgs84.CreatePolygon(new[]
                {
                    new Coordinate(-123.0, 45.3),
                    new Coordinate(-122.3, 45.3),
                    new Coordinate(-122.3, 45.7),
                    new Coordinate(-123.0, 45.7),
                    new Coordinate(-123.0, 45.3)
                })
            }
        };

        await using (var session = _store.LightweightSession())
        {
            foreach (var a in areas) session.Store(a);
            await session.SaveChangesAsync();
        }

        // Find which areas contain downtown Seattle
        var downtown = Wgs84.CreatePoint(new Coordinate(-122.33, 47.61));

        await using var q = _store.QuerySession();
        var results = await q.ContainingAsync<ServiceArea>(
            x => x.Boundary, downtown, spatialType: SpatialType.Geometry);

        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("Seattle Metro");
    }
}

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
