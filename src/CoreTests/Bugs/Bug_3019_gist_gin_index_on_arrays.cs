using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_3019_gist_gin_index_on_arrays : BugIntegrationContext
{
    [Fact]
    public async Task can_build_index()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.CreateCommand("CREATE EXTENSION if not exists pg_trgm").ExecuteNonQueryAsync();
        await conn.CreateCommand("CREATE EXTENSION if not exists btree_gin").ExecuteNonQueryAsync();

        StoreOptions(opts =>
        {
            opts.Schema.For<ProjectionModel>().Index(
                q => q.Uris,
                q => q.Method = IndexMethod.gin);
        });

        try
        {
            theSession.Store(new ProjectionModel(Guid.NewGuid()));
            await theSession.SaveChangesAsync();
        }
        catch (MissingGinExtensionException)
        {
            // this is fine too
        }
    }
}

public record ProjectionModel(Guid Id)
{
    public List<Uri> Uris { get; init; } = new();
}
