using System;
using System.Threading.Tasks;
using JasperFx;
using Marten.Schema;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;

namespace DocumentDbTests.Bugs
{
    public class Bug_1982_custom_index_with_include : BugIntegrationContext
    {
        [PgVersionTargetedFact(MinimumVersion = "11.0")]
        public async Task can_migrate_table_with_index_include_clause_from_v3_to_v4()
        {
            StoreOptions(opts =>
            {
                opts.Schema.For<Bug1982.TestDoc>()
                    .Duplicate(x => x.FirstColumn)
                    .Duplicate(x => x.SecondColumn);
            });

            await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);

            var theMapping = DocumentMapping.For<Bug1982.TestDoc>(SchemaName);

            // Add a custom index with include clause
            await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                await conn.OpenAsync();
                await conn
                    .CreateCommand($"CREATE INDEX idx_custom_with_include_clause ON {theMapping.TableName.QualifiedName} USING btree (first_column) INCLUDE (second_column);")
                    .ExecuteNonQueryAsync();
            }

            await Should.NotThrowAsync(async () => await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate));
        }
    }
}

namespace Bug1982
{
    public class TestDoc
    {
        public Guid Id { get; set; }
        public string FirstColumn { get; set; }
        public string SecondColumn { get; set; }
    }
}
