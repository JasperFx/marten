using System;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class full_text_index : IntegratedFixture
    {
        // SAMPLE: using_a_single_property_full_text_index_through_attribute_with_default
        public class UserProfile
        {
            public Guid Id { get; set; }

            [FullTextIndex]
            public string Information { get; set; }
        }

        // ENDSAMPLE

        // SAMPLE: using_a_single_property_full_text_index_through_attribute_with_custom_settings
        public class UserDetails
        {
            private const string FullTextIndexName = "mt_custom_user_details_fts_idx";

            public Guid Id { get; set; }

            [FullTextIndex(IndexName = FullTextIndexName, RegConfig = "italian")]
            public string Details { get; set; }
        }

        // ENDSAMPLE

        // SAMPLE: using_multiple_properties_full_text_index_through_attribute_with_default
        public class Article
        {
            public Guid Id { get; set; }

            [FullTextIndex]
            public string Heading { get; set; }

            [FullTextIndex]
            public string Number { get; set; }
        }

        // ENDSAMPLE

        // SAMPLE: using_multiple_properties_full_text_index_through_attribute_with_custom_settings
        public class BlogPost
        {
            public Guid Id { get; set; }

            public string Category { get; set; }

            [FullTextIndex]
            public string EnglishText { get; set; }

            [FullTextIndex(RegConfig = "italian")]
            public string ItalianText { get; set; }

            [FullTextIndex(RegConfig = "french")]
            public string FrenchText { get; set; }
        }

        // ENDSAMPLE

        [Fact]
        public void using_whole_document_full_text_index_through_store_options_with_default()
        {
            // SAMPLE: using_whole_document_full_text_index_through_store_options_with_default
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex();
            });
            // ENDSAMPLE
        }

        [Fact]
        public void using_a_single_property_full_text_index_through_store_options_with_default()
        {
            // SAMPLE: using_a_single_property_full_text_index_through_store_options_with_default
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex(d => d.FirstName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void using_a_single_property_full_text_index_through_store_options_with_custom_settings()
        {
            // SAMPLE: using_a_single_property_full_text_index_through_store_options_with_custom_settings
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex(
                    index =>
                    {
                        index.IndexName = "mt_custom_italian_user_fts_idx";
                        index.RegConfig = "italian";
                    },
                    d => d.FirstName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void using_multiple_properties_full_text_index_through_store_options_with_default()
        {
            // SAMPLE: using_multiple_properties_full_text_index_through_store_options_with_default
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex(d => d.FirstName, d => d.LastName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void using_multiple_properties_full_text_index_through_store_options_with_custom_settings()
        {
            // SAMPLE: using_multiple_properties_full_text_index_through_store_options_with_custom_settings
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex(
                    index =>
                    {
                        index.IndexName = "mt_custom_italian_user_fts_idx";
                        index.RegConfig = "italian";
                    },
                    d => d.FirstName, d => d.LastName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void using_more_than_one_full_text_index_through_store_options_with_different_reg_config()
        {
            // SAMPLE: using_more_than_one_full_text_index_through_store_options_with_different_reg_config
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>()
                    .FullTextIndex(d => d.FirstName) //by default it will use "english"
                    .FullTextIndex("italian", d => d.LastName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void example()
        {
            // SAMPLE: using-a-full-text-index
            var store = DocumentStore.For(_ =>
                                          {
                                              _.Connection(ConnectionSource.ConnectionString);

                                              // Create the full text index
                                              _.Schema.For<User>().FullTextIndex();
                                          });

            using (var session = store.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller", UserName = "jmiller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller", UserName = "lmiller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller", UserName = "mmiller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo", UserName = "fzombo" });
                session.Store(new User { FirstName = "Somebody", LastName = "Somewher", UserName = "somebody" });
                session.SaveChanges();

                var somebody = session.Search<User>("somebody");
            }

            store.Dispose();

            // ENDSAMPLE
        }

        [Fact]
        public void using_a_full_text_index_with_queryable()
        {
            // SAMPLE: using_a_full_text_index_with_queryable
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
            });

            using (var session = store.OpenSession())
            {
                session.Store(new BlogPost { Id = Guid.NewGuid(), EnglishText = "Lorem ipsum", Category = "Travel" });
                session.Store(new BlogPost { Id = Guid.NewGuid(), EnglishText = "Dolor sit", Category = "Travel" });
                session.Store(new BlogPost { Id = Guid.NewGuid(), EnglishText = "Amet", Category = "LifeStyle" });
                session.SaveChanges();

                var somebody = session.Query<BlogPost>()
                    .Where(blogPost => blogPost.Category == "Travel")
                    .Where(blogPost => blogPost.Search("Lorem"))
                    .ToList();
            }

            store.Dispose();

            // ENDSAMPLE
        }

        [Fact]
        public void should_search_with_store_options_default_configuration()
        {
            SearchShouldBeSuccessfulFor(_ => _.Schema.For<User>().FullTextIndex());
        }

        [Fact]
        public void should_search_with_store_options_for_specific_members()
        {
            SearchShouldBeSuccessfulFor(_ => _.Schema.For<User>().FullTextIndex(d => d.FirstName, d => d.LastName));
        }

        [Fact]
        public void should_search_with_store_options_with_multipleIndexes()
        {
            const string frenchRegConfig = "french";
            const string italianRegConfig = "italian";

            StoreOptions(_ => _.Schema.For<User>()
                                                     .FullTextIndex(italianRegConfig, d => d.FirstName)
                                                     .FullTextIndex(frenchRegConfig, d => d.LastName));

            const string searchFilter = "Lindsey";

            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = searchFilter, LastName = "Miller", UserName = "lmiller" });
                session.Store(new User { FirstName = "Frank", LastName = searchFilter, UserName = "fzombo" });

                session.Store(new User { FirstName = "Jeremy", LastName = "Miller", UserName = "jmiller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller", UserName = "mmiller" });
                session.Store(new User { FirstName = "Somebody", LastName = "Somewher", UserName = "somebody" });
                session.SaveChanges();

                var italianResults = session.Search<User>(searchFilter, italianRegConfig);

                italianResults.Count.ShouldBe(1);
                italianResults.ShouldContain(u => u.FirstName == searchFilter);
                italianResults.ShouldNotContain(u => u.LastName == searchFilter);

                var frenchResults = session.Search<User>(searchFilter, frenchRegConfig);

                frenchResults.Count.ShouldBe(1);
                frenchResults.ShouldNotContain(u => u.FirstName == searchFilter);
                frenchResults.ShouldContain(u => u.LastName == searchFilter);
            }
        }

        [Fact]
        public void should_search_by_tenant_with_tenancy_conjoined()
        {
            StoreOptions(_ =>
            {
                _.Events.TenancyStyle = TenancyStyle.Conjoined;
                _.Policies.AllDocumentsAreMultiTenanted();

                _.Schema.For<User>().FullTextIndex();
            });

            const string searchFilter = "Lindsey";

            var tenants = new[] { "Tenant", "Other Tenant" };

            foreach (var tenant in tenants)
            {
                using (var session = theStore.OpenSession(tenant))
                {
                    session.Store(new User { FirstName = searchFilter, LastName = "Miller", UserName = "lmiller" });
                    session.Store(new User { FirstName = "Frank", LastName = "Zombo", UserName = "fzombo" });
                    session.SaveChanges();
                }
            }

            foreach (var tenant in tenants)
            {
                using (var session = theStore.OpenSession(tenant))
                {
                    var results = session.Search<User>(searchFilter);

                    results.Count.ShouldBe(1);
                    results.ShouldContain(u => u.FirstName == searchFilter);
                    results.ShouldNotContain(u => u.LastName == searchFilter);
                }
            }
        }

        private void SearchShouldBeSuccessfulFor(Action<StoreOptions> configure)
        {
            StoreOptions(configure);

            const string searchFilter = "Lindsey";

            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = searchFilter, LastName = "Miller", UserName = "lmiller" });
                session.Store(new User { FirstName = "Frank", LastName = searchFilter, UserName = "fzombo" });

                session.Store(new User { FirstName = "Jeremy", LastName = "Miller", UserName = "jmiller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller", UserName = "mmiller" });
                session.Store(new User { FirstName = "Somebody", LastName = "Somewher", UserName = "somebody" });
                session.SaveChanges();

                var results = session.Search<User>(searchFilter);

                results.Count.ShouldBe(2);
                results.ShouldContain(u => u.FirstName == searchFilter);
                results.ShouldContain(u => u.LastName == searchFilter);
            }
        }

        [Fact]
        private void should_search_using_a_single_property_full_text_index_through_attribute_with_custom_settings()
        {
            StoreOptions(_ => _.Schema.For<UserDetails>());
        }

        [Fact]
        public void creating_a_full_text_index_should_create_the_index_on_the_table()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex());

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Where(x => x.Name == "mt_doc_target_idx_fts")
                              .Select(x => x.DDL.ToLower())
                              .First();

            ddl.ShouldContain("create index mt_doc_target_idx_fts");
            ddl.ShouldContain("on public.mt_doc_target");
            ddl.ShouldContain("to_tsvector");
        }

        [Fact]
        public void not_specifying_an_index_name_should_generate_default_index_name()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex());
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL.ToLower())
                              .First();

            ddl.ShouldContain("mt_doc_target_idx_fts");
        }

        [Fact]
        public void specifying_an_index_name_without_marten_prefix_should_prepend_prefix()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(configure: x => x.IndexName = "doesnt_have_prefix"));
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL)
                              .First();

            ddl.ShouldContain("mt_doesnt_have_prefix");
        }

        [Fact]
        public void specifying_an_index_name_with_mixed_case_should_result_in_lower_case_name()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(configure: x => x.IndexName = "Doesnt_Have_PreFix"));
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL)
                              .First();

            ddl.ShouldContain("mt_doesnt_have_prefix");
        }

        [Fact]
        public void specifying_an_index_name_with_marten_prefix_remains_unchanged()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(configure: x => x.IndexName = "mt_i_have_prefix"));
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL)
                              .First();

            ddl.ShouldContain("mt_i_have_prefix");
        }

        [Fact]
        public void specifying_an_index_name_with_marten_prefix_and_mixed_case_results_in_lowercase_name()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(configure: x => x.IndexName = "mT_I_hAve_preFIX"));
            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            var ddl = theStore.Tenancy.Default.DbObjects.AllIndexes()
                              .Select(x => x.DDL)
                              .First();

            ddl.ShouldContain("mt_i_have_prefix");
        }

        [Fact]
        public void creating_a_full_text_index_with_custom_data_configuration_should_create_the_index_with_default_regConfig_in_indexname_custom_data_configuration()
        {
            const string DataConfig = "(data ->> 'AnotherString' || ' ' || 'test')";

            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
                index =>
                {
                    index.DataConfig = DataConfig;
                }));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_doc_target_{FullTextIndex.DefaultRegConfig}_idx_fts",
                    dataConfig: DataConfig
                );
        }

        [Fact]
        public void creating_a_full_text_index_with_custom_data_configuration_and_custom_regConfig_should_create_the_index_with_custom_regConfig_in_indexname_custom_data_configuration()
        {
            const string DataConfig = "(data ->> 'AnotherString' || ' ' || 'test')";
            const string RegConfig = "french";

            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
                index =>
                {
                    index.RegConfig = RegConfig;
                    index.DataConfig = DataConfig;
                }));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_doc_target_{RegConfig}_idx_fts",
                    regConfig: RegConfig,
                    dataConfig: DataConfig
                );
        }

        [Fact]
        public void creating_a_full_text_index_with_custom_data_configuration_and_custom_regConfig_custom_indexName_should_create_the_index_with_custom_indexname_custom_data_configuration()
        {
            const string DataConfig = "(data ->> 'AnotherString' || ' ' || 'test')";
            const string RegConfig = "french";
            const string IndexName = "custom_index_name";

            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
                index =>
                {
                    index.DataConfig = DataConfig;
                    index.RegConfig = RegConfig;
                    index.IndexName = IndexName;
                }));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_{IndexName}",
                    regConfig: RegConfig,
                    dataConfig: DataConfig
                );
        }

        [Fact]
        public void creating_a_full_text_index_with_single_member_should_create_the_index_with_default_regConfig_in_indexname_and_member_selectors()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(d => d.String));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_doc_target_{FullTextIndex.DefaultRegConfig}_idx_fts",
                    dataConfig: $"((data ->> '{nameof(Target.String)}'))"
                );
        }

        [Fact]
        public void creating_a_full_text_index_with_multiple_members_should_create_the_index_with_default_regConfig_in_indexname_and_members_selectors()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(d => d.String, d => d.AnotherString));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_doc_target_{FullTextIndex.DefaultRegConfig}_idx_fts",
                    dataConfig: $"((data ->> '{nameof(Target.String)}') || ' ' || (data ->> '{nameof(Target.AnotherString)}'))"
                );
        }

        [Fact]
        public void creating_a_full_text_index_with_multiple_members_and_custom_configuration_should_create_the_index_with_custom_configuration_and_members_selectors()
        {
            const string IndexName = "custom_index_name";
            const string RegConfig = "french";

            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
                index =>
                {
                    index.IndexName = IndexName;
                    index.RegConfig = RegConfig;
                },
            d => d.AnotherString));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_{IndexName}",
                    regConfig: RegConfig,
                    dataConfig: $"((data ->> '{nameof(Target.AnotherString)}'))"
                );
        }

        [Fact]
        public void creating_multiple_full_text_index_with_different_regConfigs_and_custom_data_config_should_create_the_indexes_with_different_recConfigs()
        {
            const string frenchRegConfig = "french";
            const string italianRegConfig = "italian";

            StoreOptions(_ => _.Schema.For<Target>()
                                      .FullTextIndex(frenchRegConfig, d => d.String)
                                      .FullTextIndex(italianRegConfig, d => d.AnotherString));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_doc_target_{frenchRegConfig}_idx_fts",
                    regConfig: frenchRegConfig,
                    dataConfig: $"((data ->> '{nameof(Target.String)}'))"
                );

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_doc_target_{italianRegConfig}_idx_fts",
                    regConfig: italianRegConfig,
                    dataConfig: $"((data ->> '{nameof(Target.AnotherString)}'))"
                );
        }
    }

    public static class FullTextIndexTestsExtension
    {
        public static void ShouldContainIndexDefinitionFor<TDocument>(
            this StorageFeatures storage,
            string tableName = "public.mt_doc_target",
            string indexName = "mt_doc_target_idx_fts",
            string regConfig = "english",
            string dataConfig = null)
        {
            var ddl = storage.MappingFor(typeof(TDocument)).Indexes
                .Where(x => x.IndexName == indexName)
                .Select(x => x.ToDDL())
                .FirstOrDefault();

            ddl.ShouldNotBeNull();

            ddl.ShouldContain($"CREATE INDEX {indexName}");
            ddl.ShouldContain($"ON {tableName}");
            ddl.ShouldContain($"to_tsvector('{regConfig}', {dataConfig})");

            if (regConfig != null)
            {
                ddl.ShouldContain(regConfig);
            }

            if (dataConfig != null)
            {
                ddl.ShouldContain(dataConfig);
            }
        }
    }
}