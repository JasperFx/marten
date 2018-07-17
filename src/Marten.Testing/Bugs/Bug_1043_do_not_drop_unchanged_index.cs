using Marten.Schema;
using Marten.Storage;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1043_do_not_drop_unchanged_index : IntegratedFixture
    {
        [Fact]
        public void do_not_drop_unchanged_index()
        {
            EnableCommandLogging = true;

            StoreOptions(_ => {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;
                _.Schema.For<Bug1043.Thing>().Index(x => x.Name, x =>
                {
                    x.IndexName = "Test_Index";
                    x.IsUnique = true;
                    x.Casing = ComputedIndex.Casings.Lower;
                    x.IsConcurrent = true;
                });
            });

            using (var session = theStore.OpenSession())
            {
                session.Insert(new Bug1043.Thing
                {
                    Id = "test/1",
                    Name = "A Thing",
                    Count = 1
                });

                session.SaveChanges();
            }

            var mapping = DocumentMapping.For<Bug1043.Thing>();
            mapping.Index(x => x.Name, x =>
            {
                x.IndexName = "Test_Index";
                x.IsUnique = true;
                x.Casing = ComputedIndex.Casings.Lower;
                x.IsConcurrent = true;
            });
            var docTable = new DocumentTable(mapping);

            using (var connection = new Npgsql.NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                connection.Open();

                var diff = docTable.FetchDelta(connection);

                Assert.NotNull(diff);
                Assert.Equal(0, diff.IndexChanges.Count);
                Assert.Equal(0, diff.IndexRollbacks.Count);
            }
        }
    }
}

namespace Bug1043
{
    public class Thing
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Count { get; set; }
    }
}