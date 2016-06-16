using System.Linq;
using Marten.Schema;
using Marten.Testing.Fixtures;
using Npgsql;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Acceptance
{
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
        public void specifying_an_index_type_should_create_the_index_with_that_type()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.Number, x =>
            {
                x.Method = IndexMethod.brin;
            }));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data.ToArray());

            theStore.Schema.DbObjects.AllIndexes()
                .Where(x => x.Name == "mt_doc_target_idx_number")
                .Select(x => x.DDL.ToLower())
                .First()
                .ShouldContain("mt_doc_target_idx_number on mt_doc_target using brin");
        }
        
        [Fact]
        public void creating_index_using_date_should_work()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Index(x => x.Date);
            });

            //TODO: Phillip to finish proving his code works
            //theStore.Schema.WritePatch(@"g:\a\test-patch.sql");
            //theStore.Schema.WriteDDLByType(@"g:\a\");

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data.ToArray());

            theStore.Schema.DbObjects.AllIndexes()
                .Where(x => x.Name == "mt_doc_target_idx_date")
                .Select(x => x.DDL.ToLower())
                .First()
                .ShouldContain("mt_doc_target_idx_date on mt_doc_target");
        }

        [Fact]
        public void create_unique_index_on_string_with_mixed_casing()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.String, x =>
            {
                x.IsUnique = true;
            }));

            var testString = "MiXeD cAsE sTrInG";

            using (var session = theStore.LightweightSession())
            {
                var item = Target.GenerateRandomData(1).First();
                item.String = testString;
                session.Store(item);
                session.SaveChanges();
            }

            theStore.Schema.DbObjects.AllIndexes().Select(x => x.Name)
                    .ShouldContain("mt_doc_target_uidx_string");
            
            using (var session = theStore.LightweightSession())
            {
                var item = Target.GenerateRandomData(1).First();

                item.String = testString.ToLower();

                // Inserting the same string but all lowercase should be OK
                session.Store(item);
                session.SaveChanges();

                var item2 = Target.GenerateRandomData(1).First();

                item2.String = testString;

                // Inserting the same original string should throw
                session.Store(item2);
                Assert.Throws<NpgsqlException>(() => session.SaveChanges()).Message.ShouldContain("duplicate");
            }
        }

        [Fact]
        public void create_index_with_custom_name()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.String, x =>
            {
                x.IndexName = "banana_index_created_by_nigel";
            }));

            var testString = "MiXeD cAsE sTrInG";

            using (var session = theStore.LightweightSession())
            {
                var item = Target.GenerateRandomData(1).First();
                item.String = testString;
                session.Store(item);
                session.SaveChanges();
            }
            
            theStore.Schema.DbObjects.AllIndexes().Select(x => x.Name)
                    .ShouldContain("mt_banana_index_created_by_nigel");
        }

        [Fact]
        public void create_index_with_where_clause()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.String, x =>
            {
                x.Where = "(data ->> 'Number')::int > 10";
            }));

            var testString = "MiXeD cAsE sTrInG";

            using (var session = theStore.LightweightSession())
            {
                var item = Target.GenerateRandomData(1).First();
                item.String = testString;
                session.Store(item);
                session.SaveChanges();
            }
            
            theStore.Schema.DbObjects.AllIndexes()
                    .Where(x => x.Name == "mt_doc_target_idx_string")
                    .Select(x => x.DDL)
                    .ShouldContain(x => x.Contains("WHERE (((data ->> 'Number'::text))::integer > 10)"));
        }

        [Fact]
        public void create_unique_index_with_lower_case_constraint()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.String, x =>
            {
                x.IsUnique = true;
                x.Casing = ComputedIndex.Casings.Lower;
            }));

            var testString = "MiXeD cAsE sTrInG";

            using (var session = theStore.LightweightSession())
            {
                var item = Target.GenerateRandomData(1).First();
                item.String = testString;
                session.Store(item);
                session.SaveChanges();
            }

            theStore.Schema.DbObjects.AllIndexes().Select(x => x.Name)
                    .ShouldContain("mt_doc_target_uidx_string");

            using (var session = theStore.LightweightSession())
            {
                var item = Target.GenerateRandomData(1).First();

                item.String = testString.ToUpper();

                // Inserting the same string but all uppercase should throw because
                // the index is stored with lowcased value
                session.Store(item);
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

                patch.UpdateDDL.ShouldContain("mt_doc_target_idx_number");
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

                patch.UpdateDDL.ShouldNotContain("mt_doc_target_idx_number");
            }
        }
    }
}