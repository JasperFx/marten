using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    [Collection("acceptance")]
    public class computed_indexes: OneOffConfigurationsContext
    {
        [Fact]
        public void example()
        {
            // SAMPLE: using-a-simple-calculated-index
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

                            // Toggle whether index value will be constrained unique in scope of whole document table (Global)
                            // or in a scope of a single tenant (PerTenant)
                            // Default is Global
                            x.TenancyScope = Schema.Indexing.Unique.TenancyScope.PerTenant;

                            // Partial index by supplying a condition
                            x.Where = "(data ->> 'Number')::int > 10";
                        });

                // For B-tree indexes, it's also possible to change
                // the sort order from the default of "ascending"
                _.Schema.For<User>().Index(x => x.LastName, x =>
                        {
                            // Change the index method to "brin"
                            x.SortOrder = SortOrder.Desc;
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

            SpecificationExtensions.ShouldContain(ddl, "mt_doc_target_idx_number on");
            SpecificationExtensions.ShouldContain(ddl, "mt_doc_target using brin");
        }

        [Fact]
        public void create_index_with_sort_order()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.Number, x =>
            {
                x.SortOrder = SortOrder.Desc;
            }));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data.ToArray());

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                .Where(x => x.Name == "mt_doc_target_idx_number")
                .Select(x => x.DDL.ToLower())
                .First();

            SpecificationExtensions.ShouldContain(ddl, "mt_doc_target_idx_number on");
            SpecificationExtensions.ShouldContain(ddl, "mt_doc_target");
            ddl.ShouldEndWith(" DESC)", Case.Insensitive);
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

            SpecificationExtensions.ShouldContain(ddl, "index mt_doc_target_idx_user_idflag");

            SpecificationExtensions.ShouldContain(ddl, "((((data ->> 'userid'::text))::uuid), (((data ->> 'flag'::text))::boolean))");
        }

        [Fact]
        public void create_multi_property_string_index_with_casing()
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
            theStore.BulkInsert(data.ToArray());

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                .Single(x => x.Name == "mt_doc_target_idx_stringstring_field")
                .DDL
                .ToLower();

            SpecificationExtensions.ShouldContain(ddl, "index mt_doc_target_idx_stringstring_field");

            SpecificationExtensions.ShouldContain(ddl, "(upper((data ->> 'string'::text)), upper((data ->> 'stringfield'::text)))");
        }

        [Fact]
        public void create_multi_property_type_index_with_casing()
        {
            StoreOptions(_ =>
            {
                var columns = new Expression<Func<Target, object>>[]
                {
                    x => x.String,
                    x => x.Long,
                    x => x.OtherGuid
                };
                _.Schema.For<Target>().Index(columns, c =>
                {
                    c.Casing = ComputedIndex.Casings.Upper;
                    c.IsUnique = true;
                });
            });

            var guid = Guid.NewGuid();
            using (var session = theStore.LightweightSession())
            {
                var item = new Target
                {
                    String = "string value",
                    Long = 123,
                    OtherGuid = guid
                };
                session.Store(item);
                session.SaveChanges();
            }

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                .Single(x => x.Name == "mt_doc_target_uidx_stringlongother_guid")
                .DDL
                .ToLower();

            SpecificationExtensions.ShouldContain(ddl, "index mt_doc_target_uidx_stringlongother_guid");
            SpecificationExtensions.ShouldContain(ddl, "(upper((data ->> 'string'::text)), (((data ->> 'long'::text))::bigint), (((data ->> 'otherguid'::text))::uuid))");

            using (var session = theStore.LightweightSession())
            {
                var item = new Target
                {
                    String = "String Value",
                    Long = 123,
                    OtherGuid = guid
                };
                session.Store(item);
                var exception = Assert.Throws<DocumentAlreadyExistsException>(() => session.SaveChanges());
                Assert.Contains("duplicate key value violates unique constraint", exception.ToString());
            }
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

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                .Where(x => x.Name == "mt_doc_target_idx_date")
                .Select(x => x.DDL.ToLower())
                .First();

            SpecificationExtensions.ShouldContain(ddl, "mt_doc_target_idx_date on");
            SpecificationExtensions.ShouldContain(ddl, "mt_doc_target_idx_date");
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

                Exception<DocumentAlreadyExistsException>.ShouldBeThrownBy(() => session.SaveChanges());

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

            SpecificationExtensions.ShouldContain(theStore.Tenancy.Default.DbObjects.AllIndexes()
                        .Where(x => x.Name == "mt_doc_target_idx_string")
                        .Select(x => x.DDL), x => x.Contains("WHERE (((data ->> 'Number'::text))::integer > 10)"));
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
                Exception<DocumentAlreadyExistsException>.ShouldBeThrownBy(() => session.SaveChanges());
            }
        }

        [Fact]
        public void patch_if_missing()
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
                var patch = store2.Schema.ToPatch();

                SpecificationExtensions.ShouldContain(patch.UpdateDDL, "mt_doc_target_idx_number");
            }
        }

        [Fact]
        public void no_patch_if_not_missing()
        {
            using (var store1 = StoreOptions(_ =>
            {
                _.Schema.For<Target>().Index(x => x.Number);
            }))
            {
                store1.Advanced.Clean.CompletelyRemoveAll();

                store1.Tenancy.Default.EnsureStorageExists(typeof(Target));
            }

            using (var store2 = SeparateStore(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Target>().Index(x => x.Number);
            }))
            {
                var patch = store2.Schema.ToPatch(typeof(Target));

                SpecificationExtensions.ShouldNotContain(patch.UpdateDDL, "mt_doc_target_idx_number");
            }
        }

        public computed_indexes() : base("acceptance")
        {
        }
    }
}
