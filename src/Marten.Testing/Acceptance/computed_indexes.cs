using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Testing.Acceptance
{
    [Collection("acceptance")]
    public class computed_indexes: OneOffConfigurationsContext
    {
        [Fact]
        public void example()
        {
            #region sample_using-a-simple-calculated-index
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.DatabaseSchemaName = "examples";

                // This creates
                _.Schema.For<User>().Index(x => x.UserName);
            });

            using (var session = store.QuerySession())
            {
                // Postgresql will be able to use the computed
                // index generated from above
                var somebody = session
                    .Query<User>()
                    .FirstOrDefault(x => x.UserName == "somebody");
            }
            #endregion sample_using-a-simple-calculated-index

            store.Dispose();
        }

        [Fact]
        public async Task smoke_test()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.Number));

            var data = Target.GenerateRandomData(100).ToArray();
            await theStore.BulkInsertAsync(data.ToArray());

            var table = await theStore.Tenancy.Default.ExistingTableFor(typeof(Target));
            table.HasIndex("mt_doc_target_idx_number").ShouldBeTrue();

            using var session = theStore.QuerySession();
            var cmd = session.Query<Target>().Where(x => x.Number == 3)
                .ToCommand();

            session.Query<Target>().Where(x => x.Number == data.First().Number)
                .Select(x => x.Id).ToList().ShouldContain(data.First().Id);
        }

        [Fact]
        public void specify_a_deep_index()
        {
            #region sample_deep-calculated-index
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.Schema.For<Target>().Index(x => x.Inner.Color);
            });
            #endregion sample_deep-calculated-index
        }

        [Fact]
        public void specify_a_different_mechanism_to_customize_the_index()
        {
            #region sample_customizing-calculated-index
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
                            x.Name = "mt_my_name";

                            // Toggle whether or not the index is concurrent
                            // Default is false
                            x.IsConcurrent = true;

                            // Toggle whether or not the index is a UNIQUE
                            // index
                            x.IsUnique = true;

                            // Toggle whether index value will be constrained unique in scope of whole document table (Global)
                            // or in a scope of a single tenant (PerTenant)
                            // Default is Global
                            x.TenancyScope = Schema.Indexing.Unique.TenancyScope.PerTenant;

                            // Partial index by supplying a condition
                            x.Predicate = "(data ->> 'Number')::int > 10";
                        });

                // For B-tree indexes, it's also possible to change
                // the sort order from the default of "ascending"
                _.Schema.For<User>().Index(x => x.LastName, x =>
                        {
                            // Change the index method to "brin"
                            x.SortOrder = SortOrder.Desc;
                        });
            });
            #endregion sample_customizing-calculated-index
        }



        [Fact]
        public async Task create_multi_property_index()
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
            await theStore.BulkInsertAsync(data.ToArray());

            var table = await theStore.Tenancy.Default.ExistingTableFor(typeof(Target));
            var index = table.IndexFor("mt_doc_target_idx_user_idflag");

            index.ToDDL(table).ShouldBe("CREATE INDEX mt_doc_target_idx_user_idflag ON acceptance.mt_doc_target USING btree ((((data ->> 'UserId'::text))::uuid), (((data ->> 'Flag'::text))::boolean));");



        }

        [Fact]
        public async Task create_multi_property_string_index_with_casing()
        {
            StoreOptions(_ =>
            {
                var columns = new Expression<Func<Target, object>>[]
                {
                    x => x.String,
                    x => x.StringField
                };
                _.Schema.For<Target>().Index(columns, c => c.Casing = ComputedIndex.Casings.Upper);
            });

            var data = Target.GenerateRandomData(100).ToArray();
            await theStore.BulkInsertAsync(data.ToArray());

            var table = await theStore.Tenancy.Default.ExistingTableFor(typeof(Target));
            var index = table.IndexFor("mt_doc_target_idx_stringstring_field");

            index.ToDDL(table).ShouldBe("CREATE INDEX mt_doc_target_idx_stringstring_field ON acceptance.mt_doc_target USING btree (upper((data ->> 'String'::text)), upper((data ->> 'StringField'::text)));");


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

        }


        [Fact]
        public async Task create_index_with_custom_name()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.String, x =>
            {
                x.Name = "mt_banana_index_created_by_nigel";
            }));

            var testString = "MiXeD cAsE sTrInG";

            using (var session = theStore.LightweightSession())
            {
                var item = Target.GenerateRandomData(1).First();
                item.String = testString;
                session.Store(item);
                await session.SaveChangesAsync();
            }

            (await theStore.Tenancy.Default.ExistingTableFor(typeof(Target)))
                .HasIndex("mt_banana_index_created_by_nigel");

        }


        [Fact]
        public async Task patch_if_missing()
        {
            using (var store1 = SeparateStore())
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
                var patch = await store2.Schema.CreateMigration();

                patch.UpdateSql.ShouldContain( "mt_doc_target_idx_number", Case.Insensitive);
            }
        }

        public computed_indexes() : base("acceptance")
        {
        }
    }
}
