using System.Linq;
using Baseline;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Acceptance
{
    using System;
    using Marten.Schema;
    using Npgsql;

    public class computed_indexes : IntegratedFixture
    {
        private readonly ITestOutputHelper _output;

        public computed_indexes(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void smoke_test()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.Number));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data.ToArray());

            theStore.Schema.DbObjects.AllIndexes().Select(x => x.Name)
                .ShouldContain("mt_doc_target_idx_number");


            using (var session = theStore.QuerySession())
            {
                var cmd = session.Query<Target>().Where(x => x.Number == 3)
                                 .ToCommand();

                // I used this to manually verify that the index was used in the query
                // by doing Analyze in PGAdmin III
                _output.WriteLine(cmd.CommandText);

                session.Query<Target>().Where(x => x.Number == data.First().Number)
                       .Select(x => x.Id).ToList().ShouldContain(data.First().Id);
            }
        }

        [Fact]
        public void create_unique()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.String, x =>
            {
                x.IsUnique = true;
                x.Casing = ComputedIndex.Casings.Lower;
            }));

            var data = Target.GenerateRandomData(10).ToArray();
            data.Each(x => x.String += Guid.NewGuid().ToString());
            theStore.BulkInsert(data.ToArray());

            theStore.Schema.DbObjects.AllIndexes().Select(x => x.Name)
                    .ShouldContain("mt_doc_target_uidx_string");
            
            using (var session = theStore.LightweightSession())
            {
                var item = session.Query<Target>().First(x => x.Number > 0);

                var newItem = Target.GenerateRandomData(1).First();

                newItem.String = item.String;

                session.Store(newItem);

                Assert.Throws<NpgsqlException>(() => session.SaveChanges()).Message.ShouldContain("duplicate");
            }
        }

        [Fact]
        public void patch_if_missing()
        {
            using (var store1 = TestingDocumentStore.Basic())
            {
                store1.Advanced.Clean.CompletelyRemoveAll();

                store1.Schema.EnsureStorageExists(typeof(Target));
            }

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Target>().Index(x => x.Number);
            }))
            {
                var patch = store2.Schema.ToPatch();

                patch.ShouldContain("mt_doc_target_idx_number");
            }
        }

        [Fact]
        public void no_patch_if_not_missing()
        {
            using (var store1 = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<Target>().Index(x => x.Number);
            }))
            {
                store1.Advanced.Clean.CompletelyRemoveAll();

                store1.Schema.EnsureStorageExists(typeof(Target));
            }

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Target>().Index(x => x.Number);
            }))
            {
                var patch = store2.Schema.ToPatch();

                patch.ShouldNotContain("mt_doc_target_idx_number");
            }
        }
    }
}