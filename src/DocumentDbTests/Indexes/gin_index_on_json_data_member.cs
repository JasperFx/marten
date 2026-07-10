using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Indexes;

public class gin_index_on_json_data_member: OneOffConfigurationsContext
{
    [Fact]
    public void generates_expression_gin_index_ddl()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<GinOrder>().GinIndexJsonDataMember(x => x.Lines);
        });

        var mapping = theStore.StorageFeatures.MappingFor(typeof(GinOrder));
        var index = mapping.Indexes.OfType<Marten.Schema.DocumentIndex>()
            .Single(x => x.Method == IndexMethod.gin);

        var ddl = index.ToDDL(theStore.StorageFeatures.MappingFor(typeof(GinOrder)).Schema.Table);

        ddl.ShouldContain("USING gin");
        ddl.ShouldContain("(data -> 'Lines') jsonb_path_ops");
    }

    [Fact]
    public async Task database_round_trip_is_idempotent()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<GinOrder>().GinIndexJsonDataMember(x => x.Lines);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // a canonicalization mismatch between our DDL and what Postgres reports
        // back would flag a spurious index rebuild here
        await Should.NotThrowAsync(async () =>
            await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    [Fact]
    public async Task containment_query_uses_the_expression_index()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<GinOrder>().GinIndexJsonDataMember(x => x.Lines);
        });

        var orders = Enumerable.Range(0, 100).Select(i => new GinOrder
        {
            Id = Guid.NewGuid(),
            Lines = new List<GinOrderLine> { new() { ItemName = $"item-{i % 10}" } }
        }).ToArray();

        await theStore.BulkInsertDocumentsAsync(orders);

        await using var session = theStore.QuerySession();

        // correctness through the index
        var hits = await session.Query<GinOrder>()
            .Where(x => x.Lines.Any(l => l.ItemName == "item-3"))
            .ToListAsync();
        hits.Count.ShouldBe(10);

        // prove the planner can use the expression index for the containment filter
        var cmd = session.Query<GinOrder>()
            .Where(x => x.Lines.Any(l => l.ItemName == "item-3"))
            .ToCommand();

        var plan = new List<string>();
        var conn = new Npgsql.NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try
        {
            await using (var setup = conn.CreateCommand())
            {
                setup.CommandText = "SET enable_seqscan = off";
                await setup.ExecuteNonQueryAsync();
            }

            await using var explain = conn.CreateCommand();
            explain.CommandText = "EXPLAIN " + cmd.CommandText;
            foreach (Npgsql.NpgsqlParameter p in cmd.Parameters)
            {
                explain.Parameters.Add(new Npgsql.NpgsqlParameter(p.ParameterName, p.NpgsqlDbType)
                {
                    Value = p.Value
                });
            }

            await using var reader = await explain.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }
        finally
        {
            await conn.CloseAsync();
            await conn.DisposeAsync();
        }

        var planText = string.Join("\n", plan);
        planText.ShouldContain("_idx_lines_gin");
    }

    [Fact]
    public void nested_member_paths_walk_the_arrow_chain()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<GinOrder>().GinIndexJsonDataMember(x => x.Customer.Addresses);
        });

        var mapping = theStore.StorageFeatures.MappingFor(typeof(GinOrder));
        var index = mapping.Indexes.OfType<Marten.Schema.DocumentIndex>()
            .Single(x => x.Method == IndexMethod.gin);

        var ddl = index.ToDDL(mapping.Schema.Table);
        ddl.ShouldContain("(data -> 'Customer' -> 'Addresses') jsonb_path_ops");
    }
}

public class GinOrder
{
    public Guid Id { get; set; }
    public List<GinOrderLine> Lines { get; set; } = new();
    public GinCustomer Customer { get; set; } = new();
}

public class GinOrderLine
{
    public string ItemName { get; set; }
}

public class GinCustomer
{
    public List<string> Addresses { get; set; } = new();
}
