using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Bugs;

public class advanced_sql_query_scalar_reference_types: BugIntegrationContext
{
    private readonly byte[] theData = Encoding.UTF8.GetBytes("hello blob");
    private readonly IPAddress theUploadedFrom = IPAddress.Parse("203.0.113.42");
    private readonly Guid theId = Guid.NewGuid();

    private async Task seedBlobTableAsync()
    {
        StoreOptions(opts =>
        {
            // Plain table, deliberately not a Marten document, so its bytes live in a
            // real `bytea` column instead of base64-in-JSONB. Schema-qualified with the
            // test's own schema so BugIntegrationContext's per-test schema drop cleans it up.
            var blobTable = new Table(new PostgresqlObjectName(SchemaName, "blob", SchemaUtils.IdentifierUsage.General));
            blobTable.AddColumn<Guid>("id").AsPrimaryKey();

            // Explicit "bytea" pg type: Weasel's generic AddColumn<byte[]>() maps byte[] to
            // a Postgres smallint[] array (element-wise), not bytea.
            blobTable.AddColumn("data", "bytea").NotNull();
            blobTable.AddColumn<string>("media_type").NotNull();
            blobTable.AddColumn<int>("size").NotNull();

            // Second reference-type scalar (a real Postgres "inet" column, not bytea) to prove the
            // fix isn't byte[]-specific: any reference type with a direct Npgsql scalar mapping
            // (byte[], BitArray, JsonDocument, IPAddress, PhysicalAddress, ...) hit the same
            // ScalarSelectClause<T> where T : struct crash. Note this does *not* extend to CLR
            // arrays like int[]/Guid[] — Npgsql composes those from their element type rather than
            // registering a scalar type mapping, so HasTypeMapping(typeof(int[])) is false and they
            // never reach this code path at all.
            blobTable.AddColumn<IPAddress>("uploaded_from").NotNull();

            opts.Storage.ExtendedSchemaObjects.Add(blobTable);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"insert into {SchemaName}.blob (id, data, media_type, size, uploaded_from) values (@id, @data, @media_type, @size, @uploaded_from)";
        cmd.Parameters.AddWithValue("id", theId);
        cmd.Parameters.AddWithValue("data", theData);
        cmd.Parameters.AddWithValue("media_type", "text/plain");
        cmd.Parameters.AddWithValue("size", theData.Length);
        cmd.Parameters.AddWithValue("uploaded_from", theUploadedFrom);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task can_query_single_bytea_scalar()
    {
        await seedBlobTableAsync();

        await using var session = theStore.QuerySession();
        var data = (await session.AdvancedSql.QueryAsync<byte[]>(
            $"select data from {SchemaName}.blob where id = ?", CancellationToken.None, theId)).Single();

        data.ShouldBe(theData);
    }

    [Fact]
    public async Task can_query_single_inet_scalar()
    {
        await seedBlobTableAsync();

        await using var session = theStore.QuerySession();
        var uploadedFrom = (await session.AdvancedSql.QueryAsync<IPAddress>(
            $"select uploaded_from from {SchemaName}.blob where id = ?", CancellationToken.None, theId)).Single();

        uploadedFrom.ShouldBe(theUploadedFrom);
    }

    [Fact]
    public async Task can_query_bytea_column_in_row_tuple()
    {
        await seedBlobTableAsync();

        await using var session = theStore.QuerySession();
        var (id, data, mediaType) = (await session.AdvancedSql.QueryAsync<Guid, byte[], string>(
            $"select row(id), row(data), row(media_type) from {SchemaName}.blob where id = ?",
            CancellationToken.None, theId)).Single();

        id.ShouldBe(theId);
        data.ShouldBe(theData);
        mediaType.ShouldBe("text/plain");
    }

    [Fact]
    public async Task can_stream_bytea_scalar()
    {
        await seedBlobTableAsync();

        await using var session = theStore.QuerySession();
        var asyncEnumerable = session.AdvancedSql.StreamAsync<byte[]>(
            $"select data from {SchemaName}.blob where id = ?", CancellationToken.None, theId);

        var results = new List<byte[]>();
        await foreach (var data in asyncEnumerable)
        {
            results.Add(data);
        }

        results.Single().ShouldBe(theData);
    }
}
