using System;
using System.Linq;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Important
    {
        public Guid Id { get; set; }
        public string Field1 { get; set; }
        public string Field2 { get; set; }
    }

    public class Bug_960_drop_index_concurrently_pg_error : IntegratedFixture
    {
        /// <summary>
        /// Fix for PG error "0A000: DROP INDEX CONCURRENTLY must be first action in transaction"
        /// </summary>
        [Fact]
        public async void can_work_after_adding_a_new_index_and_dropping_an_existing_one()
        {
            // Create store with index on Field 1
            var store1 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Important>().Index(x => x.Field1);
            });

            // cleanup prior to starting
            store1.Advanced.Clean.CompletelyRemoveAll();

            using (var session = store1.LightweightSession())
            {
                session.Store(new Important()
                {
                    Field1 = "field1-value",
                    Field2 = "field2.value"
                });
                session.SaveChanges();
            }

            // Add index on Field2 but exclude index on field 1.
            // Prior to fix, this throws error "0A000: DROP INDEX CONCURRENTLY must be first action in transaction"
            var store2 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Important>().Index(x => x.Field2);
            });

            
            using (var session = store2.LightweightSession())
            {
                await session.Query<Important>()
                .Where(p => p.Field1 == "some value")
                    .ToListAsync();
            }
        }
    }
}