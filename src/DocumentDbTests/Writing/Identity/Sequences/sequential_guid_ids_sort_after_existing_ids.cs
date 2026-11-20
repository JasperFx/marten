using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

/// <summary>
/// Marten 10 makes new Guid ids with JasperFx's <c>SequentialGuid.NextPostgresOrder()</c>. They sort strictly in the
/// order they were made, the way PostgreSQL compares a <c>uuid</c>, and after the version 7 and COMB ids a Marten 9
/// store wrote.
/// </summary>
public class sequential_guid_ids_sort_after_existing_ids: OneOffConfigurationsContext
{
    public class Ticket
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
    }

    [Fact]
    public async Task new_document_ids_sort_in_creation_order_in_postgresql_and_after_a_version_7_id()
    {
        StoreOptions(opts => opts.Schema.For<Ticket>());

        var older = Guid.CreateVersion7();

        await using (var session = theStore.LightweightSession())
        {
            // One at a time, so every id is made after the one before it
            for (var i = 0; i < 50; i++)
            {
                session.Store(new Ticket { Number = i });
            }

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        var table = theStore.Options.Storage.MappingFor(typeof(Ticket)).TableName.QualifiedName;
        await using var cmd = new NpgsqlCommand(
            $"select (data ->> 'Number')::int, id > @older from {table} order by id", conn);
        cmd.Parameters.AddWithValue("older", older);

        var numbers = new System.Collections.Generic.List<int>();
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            numbers.Add(reader.GetInt32(0));
            reader.GetBoolean(1).ShouldBeTrue("every new id sorts after an earlier version 7 id");
        }

        numbers.ShouldBe(Enumerable.Range(0, 50).ToList());
    }
}
