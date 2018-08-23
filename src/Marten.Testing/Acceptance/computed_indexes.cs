using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using Marten.Schema;
using Marten.Testing.Documents;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class computed_indexes : IntegratedFixture
    {
        [Fact]
        public void example()
        {
            // SAMPLE: using-a-simple-calculated-index
    var store = DocumentStore.For(_ =>
    {
        _.Connection(ConnectionSource.ConnectionString);

        // This creates 
        _.Schema.For<User>().Index(x => x.UserName);
    });

            

    using (var session = store.QuerySession())
    {
        // Postgresql will be able to use the computed
        // index generated from above
        var somebody = session.Query<User>()
            .Where(x => x.UserName == "somebody")
            .FirstOrDefault();
    }
            // ENDSAMPLE

            store.Dispose();
        }

        [Fact]
        public void smoke_test()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.Number));

                        var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data.ToArray());

            theStore.Tenancy.Default.DbObjects.AllIndexes().Select(x => x.Name)
                .ShouldContain("mt_doc_target_idx_number");

            using (var session = theStore.QuerySession())
            {
                var cmd = session.Query<Target>().Where(x => x.Number == 3)
                                 .ToCommand();

                session.Query<Target>().Where(x => x.Number == data.First().Number)
                       .Select(x => x.Id).ToList().ShouldContain(data.First().Id);
            }
        }

        [Fact]
        public void specify_a_deep_index()
        {
            // SAMPLE: deep-calculated-index
    var store = DocumentStore.For(_ =>
    {
        _.Connection(ConnectionSource.ConnectionString);

        _.Schema.For<Target>().Index(x => x.Inner.Color);
    });
            // ENDSAMPLE

        }

        [Fact]
        public void specify_a_different_mechanism_to_customize_the_index()
        {
            // SAMPLE: customizing-calculated-index
    var store = DocumentStore.For(_ =>
    {
        _.Connection(ConnectionSource.ConnectionString);

        // The second, optional argument to Index()
        // allows you to customize the calculated index
        _.Schema.For<Target>().Index(x => x.Number, x =>
        {
            // Change the index method to "brin"
            x.Method = IndexMethod.brin;

            // Force the index to be generated with casing rules
            x.Casing = ComputedIndex.Casings.Lower;

            // Override the index name if you want
            x.IndexName = "mt_my_name";

            // Toggle whether or not the index is concurrent
            // Default is false
            x.IsConcurrent = true;

            // Toggle whether or not the index is a UNIQUE
            // index
            x.IsUnique = true;

            // Partial index by supplying a condition
            x.Where = "(data ->> 'Number')::int > 10";
        });
    });
            // ENDSAMPLE
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

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                .Where(x => x.Name == "mt_doc_target_idx_number")
                .Select(x => x.DDL.ToLower())
                .First();
            
            ddl.ShouldContain("mt_doc_target_idx_number on");
            ddl.ShouldContain("mt_doc_target using brin");
        }

        [Fact]
        public void create_multi_property_index()
        {
            StoreOptions(_ =>
            {
                var columns = new Expression<Func<Target, object>>[]
                {
                    x => x.UserId,
                    x => x.Flag
                };
                _.Schema.For<Target>().Index(columns);
            });

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data.ToArray());

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                .Single(x => x.Name == "mt_doc_target_idx_user_idflag")
                .DDL
                .ToLower();

            ddl.ShouldContain("index mt_doc_target_idx_user_idflag on");
            ddl.ShouldContain("((((data ->> 'userid'::text))::uuid), (((data ->> 'flag'::text))::boolean))");
        }

        [Fact]
        public void creating_index_using_date_should_work()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Index(x => x.Date);
            });

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data.ToArray());

            theStore.Tenancy.Default.DbObjects.AllIndexes()
                .Where(x => x.Name == "mt_doc_target_idx_date")
                .Select(x => x.DDL.ToLower())
                .First()
                .ShouldContain("mt_doc_target_idx_date on");
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

            theStore.Tenancy.Default.DbObjects.AllIndexes().Select(x => x.Name)
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
                Assert.Throws<MartenCommandException>(() => session.SaveChanges()).Message.ShouldContain("duplicate");
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
            
            theStore.Tenancy.Default.DbObjects.AllIndexes().Select(x => x.Name)
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
            
            theStore.Tenancy.Default.DbObjects.AllIndexes()
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

            theStore.Tenancy.Default.DbObjects.AllIndexes().Select(x => x.Name)
                    .ShouldContain("mt_doc_target_uidx_string");

            using (var session = theStore.LightweightSession())
            {
                var item = Target.GenerateRandomData(1).First();

                item.String = testString.ToUpper();

                // Inserting the same string but all uppercase should throw because
                // the index is stored with lowcased value
                session.Store(item);
                Assert.Throws<MartenCommandException>(() => session.SaveChanges()).Message.ShouldContain("duplicate");
            }
        }

        [Fact]
        public void patch_if_missing()
        {
            using (var store1 = TestingDocumentStore.Basic())
            {
                store1.Advanced.Clean.CompletelyRemoveAll();

                store1.Tenancy.Default.EnsureStorageExists(typeof(Target));
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

                store1.Tenancy.Default.EnsureStorageExists(typeof(Target));
            }

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Target>().Index(x => x.Number);
            }))
            {
                var patch = store2.Schema.ToPatch(typeof(Target));

                patch.UpdateDDL.ShouldNotContain("mt_doc_target_idx_number");
            }
        }
    }
}