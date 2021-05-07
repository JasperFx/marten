using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    #region sample_using_a_full_text_index_through_attribute_on_class_with_default
    [FullTextIndex]
    public class Book
    {
        public Guid Id { get; set; }

        public string Title { get; set; }

        public string Author { get; set; }

        public string Information { get; set; }
    }

    #endregion sample_using_a_full_text_index_through_attribute_on_class_with_default

    #region sample_using_a_single_property_full_text_index_through_attribute_with_default
    public class UserProfile
    {
        public Guid Id { get; set; }

        [FullTextIndex]
        public string Information { get; set; }
    }

    #endregion sample_using_a_single_property_full_text_index_through_attribute_with_default

    #region sample_using_a_single_property_full_text_index_through_attribute_with_custom_settings
    public class UserDetails
    {
        private const string FullTextIndexName = "mt_custom_user_details_fts_idx";

        public Guid Id { get; set; }

        [FullTextIndex(IndexName = FullTextIndexName, RegConfig = "italian")]
        public string Details { get; set; }
    }

    #endregion sample_using_a_single_property_full_text_index_through_attribute_with_custom_settings

    #region sample_using_multiple_properties_full_text_index_through_attribute_with_default
    public class Article
    {
        public Guid Id { get; set; }

        [FullTextIndex]
        public string Heading { get; set; }

        [FullTextIndex]
        public string Text { get; set; }
    }

    #endregion sample_using_multiple_properties_full_text_index_through_attribute_with_default

    #region sample_using_multiple_properties_full_text_index_through_attribute_with_custom_settings
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

    #endregion sample_using_multiple_properties_full_text_index_through_attribute_with_custom_settings

    [Collection("fulltext")]
    public class full_text_index: OneOffConfigurationsContext
    {
        public full_text_index() : base("fulltext")
        {
        }

        public void using_whole_document_full_text_index_through_store_options_with_default()
        {
            #region sample_using_whole_document_full_text_index_through_store_options_with_default
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex();
            });
            #endregion sample_using_whole_document_full_text_index_through_store_options_with_default
        }

        public void using_a_single_property_full_text_index_through_store_options_with_default()
        {
            #region sample_using_a_single_property_full_text_index_through_store_options_with_default
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex(d => d.FirstName);
            });
            #endregion sample_using_a_single_property_full_text_index_through_store_options_with_default
        }

        public void using_a_single_property_full_text_index_through_store_options_with_custom_settings()
        {
            #region sample_using_a_single_property_full_text_index_through_store_options_with_custom_settings
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex(
                    index =>
                    {
                        index.Name = "mt_custom_italian_user_fts_idx";
                        index.RegConfig = "italian";
                    },
                    d => d.FirstName);
            });
            #endregion sample_using_a_single_property_full_text_index_through_store_options_with_custom_settings
        }

        public void using_multiple_properties_full_text_index_through_store_options_with_default()
        {
            #region sample_using_multiple_properties_full_text_index_through_store_options_with_default
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex(d => d.FirstName, d => d.LastName);
            });
            #endregion sample_using_multiple_properties_full_text_index_through_store_options_with_default
        }

        public void using_multiple_properties_full_text_index_through_store_options_with_custom_settings()
        {
            #region sample_using_multiple_properties_full_text_index_through_store_options_with_custom_settings
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>().FullTextIndex(
                    index =>
                    {
                        index.Name = "mt_custom_italian_user_fts_idx";
                        index.RegConfig = "italian";
                    },
                    d => d.FirstName, d => d.LastName);
            });
            #endregion sample_using_multiple_properties_full_text_index_through_store_options_with_custom_settings
        }

        public void using_more_than_one_full_text_index_through_store_options_with_different_reg_config()
        {
            #region sample_using_more_than_one_full_text_index_through_store_options_with_different_reg_config
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                // This creates
                _.Schema.For<User>()
                    .FullTextIndex(d => d.FirstName) //by default it will use "english"
                    .FullTextIndex("italian", d => d.LastName);
            });
            #endregion sample_using_more_than_one_full_text_index_through_store_options_with_different_reg_config
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void using_full_text_query_through_query_session()
        {
            #region sample_using_full_text_query_through_query_session
            var store = DocumentStore.For(_ =>
                                          {
                                              _.Connection(ConnectionSource.ConnectionString);

                                              // Create the full text index
                                              _.Schema.For<User>().FullTextIndex();

                                              _.DatabaseSchemaName = "fulltext";
                                          });
            IReadOnlyList<User> result;

            using (var session = store.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller", UserName = "jmiller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller", UserName = "lmiller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller", UserName = "mmiller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo", UserName = "fzombo" });
                session.Store(new User { FirstName = "Somebody", LastName = "Somewher", UserName = "somebody" });
                session.SaveChanges();

                result = session.Search<User>("somebody");
            }

            store.Dispose();

            #endregion sample_using_full_text_query_through_query_session

            result.Count().ShouldBe(1);
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void search_in_query_sample()
        {
            StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

            var expectedId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter" });
                session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter" });
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                #region sample_search_in_query_sample
                var posts = session.Query<BlogPost>()
                    .Where(x => x.Search("somefilter"))
                    .ToList();
                #endregion sample_search_in_query_sample

                posts.Count.ShouldBe(1);
                posts.Single().Id.ShouldBe(expectedId);
            }
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void plain_text_search_in_query_sample()
        {
            StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

            var expectedId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter" });
                session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter" });
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                #region sample_plain_search_in_query_sample
                var posts = session.Query<BlogPost>()
                    .Where(x => x.PlainTextSearch("somefilter"))
                    .ToList();
                #endregion sample_plain_search_in_query_sample

                posts.Count.ShouldBe(1);
                posts.Single().Id.ShouldBe(expectedId);
            }
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void phrase_search_in_query_sample()
        {
            StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

            var expectedId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter" });
                session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter" });
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                #region sample_phrase_search_in_query_sample
                var posts = session.Query<BlogPost>()
                    .Where(x => x.PhraseSearch("somefilter"))
                    .ToList();
                #endregion sample_phrase_search_in_query_sample

                posts.Count.ShouldBe(1);
                posts.Single().Id.ShouldBe(expectedId);
            }
        }

        [PgVersionTargetedFact(MinimumVersion = "11.0")]
        public void web_search_in_query_sample()
        {
            StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

            var expectedId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter" });
                session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter" });
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                #region sample_web_search_in_query_sample
                var posts = session.Query<BlogPost>()
                    .Where(x => x.WebStyleSearch("somefilter"))
                    .ToList();
                #endregion sample_web_search_in_query_sample

                posts.Count.ShouldBe(1);
                posts.Single().Id.ShouldBe(expectedId);
            }
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void text_search_combined_with_other_query_sample()
        {
            StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

            var expectedId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter", Category = "LifeStyle" });
                session.Store(new BlogPost { Id = Guid.NewGuid(), EnglishText = "somefilter", Category = "Other" });
                session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter", Category = "LifeStyle" });
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                #region sample_text_search_combined_with_other_query_sample
                var posts = session.Query<BlogPost>()
                    .Where(x => x.Category == "LifeStyle")
                    .Where(x => x.PhraseSearch("somefilter"))
                    .ToList();
                #endregion sample_text_search_combined_with_other_query_sample

                posts.Count.ShouldBe(1);
                posts.Single().Id.ShouldBe(expectedId);
            }
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void text_search_with_non_default_regConfig_sample()
        {
            StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

            var expectedId = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.Store(new BlogPost { Id = Guid.NewGuid(), EnglishText = "somefilter" });
                session.Store(new BlogPost { Id = expectedId, ItalianText = "somefilter" });
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                #region sample_text_search_with_non_default_regConfig_sample
                var posts = session.Query<BlogPost>()
                    .Where(x => x.PhraseSearch("somefilter", "italian"))
                    .ToList();
                #endregion sample_text_search_with_non_default_regConfig_sample

                posts.Count.ShouldBe(1);
                posts.Single().Id.ShouldBe(expectedId);
            }
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void should_search_with_store_options_default_configuration()
        {
            SearchShouldBeSuccessfulFor(_ => _.Schema.For<User>().FullTextIndex());
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void should_search_with_store_options_for_specific_members()
        {
            SearchShouldBeSuccessfulFor(_ => _.Schema.For<User>().FullTextIndex(d => d.FirstName, d => d.LastName));
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
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
                SpecificationExtensions.ShouldContain(italianResults, u => u.FirstName == searchFilter);
                italianResults.ShouldNotContain(u => u.LastName == searchFilter);

                var frenchResults = session.Search<User>(searchFilter, frenchRegConfig);

                frenchResults.Count.ShouldBe(1);
                frenchResults.ShouldNotContain(u => u.FirstName == searchFilter);
                SpecificationExtensions.ShouldContain(frenchResults, u => u.LastName == searchFilter);
            }
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
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
                    SpecificationExtensions.ShouldContain(results, u => u.FirstName == searchFilter);
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
                SpecificationExtensions.ShouldContain(results, u => u.FirstName == searchFilter);
                SpecificationExtensions.ShouldContain(results, u => u.LastName == searchFilter);
            }
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        private void should_search_using_a_single_property_full_text_index_through_attribute_with_custom_settings()
        {
            StoreOptions(_ => _.Schema.For<UserDetails>());
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public async Task creating_a_full_text_index_should_create_the_index_on_the_table()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex());

            var data = Target.GenerateRandomData(100).ToArray();
            await theStore.BulkInsertAsync(data);

            var table = await theStore.Tenancy.Default.ExistingTableFor(typeof(Target));
            var index = table.IndexFor("mt_doc_target_idx_fts");
            index.ShouldNotBeNull();

            index.ToDDL(table).ShouldContain("to_tsvector", StringComparisonOption.Default);

        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void creating_a_full_text_index_with_custom_data_configuration_should_create_the_index_without_regConfig_in_indexname_custom_data_configuration()
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
                    indexName: $"mt_doc_target_idx_fts",
                    dataConfig: DataConfig
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
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

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
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
                    index.Name = IndexName;
                }));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: IndexName,
                    regConfig: RegConfig,
                    dataConfig: DataConfig
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void creating_a_full_text_index_with_single_member_should_create_the_index_without_regConfig_in_indexname_and_member_selectors()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(d => d.String));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_doc_target_idx_fts",
                    dataConfig: $"((data ->> '{nameof(Target.String)}'))"
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void creating_a_full_text_index_with_multiple_members_should_create_the_index_without_regConfig_in_indexname_and_members_selectors()
        {
            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(d => d.String, d => d.AnotherString));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: $"mt_doc_target_idx_fts",
                    dataConfig: $"((data ->> '{nameof(Target.String)}') || ' ' || (data ->> '{nameof(Target.AnotherString)}'))"
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void creating_a_full_text_index_with_multiple_members_and_custom_configuration_should_create_the_index_with_custom_configuration_and_members_selectors()
        {
            const string IndexName = "custom_index_name";
            const string RegConfig = "french";

            StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
                index =>
                {
                    index.Name = IndexName;
                    index.RegConfig = RegConfig;
                },
            d => d.AnotherString));

            var data = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(data);

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Target>(
                    indexName: IndexName,
                    regConfig: RegConfig,
                    dataConfig: $"((data ->> '{nameof(Target.AnotherString)}'))"
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
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

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void using_a_full_text_index_through_attribute_on_class_with_default()
        {
            StoreOptions(_ => _.RegisterDocumentType<Book>());

            theStore.BulkInsert(new[] { new Book { Id = Guid.NewGuid(), Author = "test", Information = "test", Title = "test" } });

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Book>(
                    tableName: "fulltext.mt_doc_book",
                    indexName: $"mt_doc_book_idx_fts",
                    regConfig: FullTextIndex.DefaultRegConfig,
                    dataConfig: $"data"
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void using_a_single_property_full_text_index_through_attribute_with_default()
        {
            StoreOptions(_ => _.RegisterDocumentType<UserProfile>());

            theStore.BulkInsert(new[] { new UserProfile { Id = Guid.NewGuid(), Information = "test" } });

            theStore.Storage
                .ShouldContainIndexDefinitionFor<UserProfile>(
                    tableName: "fulltext.mt_doc_userprofile",
                    indexName: $"mt_doc_userprofile_idx_fts",
                    regConfig: FullTextIndex.DefaultRegConfig,
                    dataConfig: $"((data ->> '{nameof(UserProfile.Information)}'))"
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void using_a_single_property_full_text_index_through_attribute_with_custom_settings()
        {
            StoreOptions(_ => _.RegisterDocumentType<UserDetails>());

            theStore.BulkInsert(new[] { new UserDetails { Id = Guid.NewGuid(), Details = "test" } });

            theStore.Storage
                .ShouldContainIndexDefinitionFor<UserDetails>(
                    tableName: "fulltext.mt_doc_userdetails",
                    indexName: "mt_custom_user_details_fts_idx",
                    regConfig: "italian",
                    dataConfig: $"((data ->> '{nameof(UserDetails.Details)}'))"
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void using_multiple_properties_full_text_index_through_attribute_with_default()
        {
            StoreOptions(_ => _.RegisterDocumentType<Article>());

            theStore.BulkInsert(new[] { new Article { Id = Guid.NewGuid(), Heading = "test", Text = "test" } });

            theStore.Storage
                .ShouldContainIndexDefinitionFor<Article>(
                    tableName: "fulltext.mt_doc_article",
                    indexName: $"mt_doc_article_idx_fts",
                    regConfig: FullTextIndex.DefaultRegConfig,
                    dataConfig: $"((data ->> '{nameof(Article.Heading)}') || ' ' || (data ->> '{nameof(Article.Text)}'))"
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public void using_multiple_properties_full_text_index_through_attribute_with_custom_settings()
        {
            const string frenchRegConfig = "french";
            const string italianRegConfig = "italian";

            StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

            theStore.BulkInsert(new[] { new BlogPost { Id = Guid.NewGuid(), Category = "test", EnglishText = "test", FrenchText = "test", ItalianText = "test" } });

            theStore.Storage
                .ShouldContainIndexDefinitionFor<BlogPost>(
                    tableName: "fulltext.mt_doc_blogpost",
                    indexName: $"mt_doc_blogpost_idx_fts",
                    regConfig: FullTextIndex.DefaultRegConfig,
                    dataConfig: $"((data ->> '{nameof(BlogPost.EnglishText)}'))"
                );

            theStore.Storage
                .ShouldContainIndexDefinitionFor<BlogPost>(
                    tableName: "fulltext.mt_doc_blogpost",
                    indexName: $"mt_doc_blogpost_{frenchRegConfig}_idx_fts",
                    regConfig: frenchRegConfig,
                    dataConfig: $"((data ->> '{nameof(BlogPost.FrenchText)}'))"
                );

            theStore.Storage
                .ShouldContainIndexDefinitionFor<BlogPost>(
                    tableName: "fulltext.mt_doc_blogpost",
                    indexName: $"mt_doc_blogpost_{italianRegConfig}_idx_fts",
                    regConfig: italianRegConfig,
                    dataConfig: $"((data ->> '{nameof(BlogPost.ItalianText)}'))"
                );
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public async Task wholedoc_fts_index_comparison_works()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>().FullTextIndex();
            });

            // Apply changes
            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            // Look at updates after that
            var patch = await theStore.Schema.CreateMigration();

            Assert.DoesNotContain("drop index fulltext.mt_doc_user_idx_fts", patch.UpdateSql);
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public async Task fts_index_comparison_must_take_into_account_automatic_cast()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Company>()
                    .FullTextIndex(x => x.Name);
            });

            // Apply changes
            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            // Look at updates after that
            var patch = await theStore.Schema.CreateMigration();

            Assert.DoesNotContain("drop index fulltext.mt_doc_company_idx_fts", patch.UpdateSql);
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public async Task multifield_fts_index_comparison_must_take_into_account_automatic_cast()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .FullTextIndex(x => x.FirstName, x => x.LastName);
            });

            // Apply changes
            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            // Look at updates after that
            var patch = await theStore.Schema.CreateMigration();

            Assert.DoesNotContain("drop index fulltext.mt_doc_user_idx_fts", patch.UpdateSql);
        }

        [PgVersionTargetedFact(MinimumVersion = "10.0")]
        public async Task modified_fts_index_comparison_must_generate_drop()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .FullTextIndex(x => x.FirstName);
            });

            // Apply changes
            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            // Change indexed fields
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = "fulltext";

                _.Schema.For<User>()
                    .FullTextIndex(x => x.FirstName, x => x.LastName);
            });

            // Look at updates after that
            var patch = await store.Schema.CreateMigration();

            Assert.Contains("drop index concurrently if exists fulltext.mt_doc_user_idx_fts", patch.UpdateSql);
        }

    }

    public static class FullTextIndexTestsExtension
    {
        public static void ShouldContainIndexDefinitionFor<TDocument>(
            this StorageFeatures storage,
            string tableName = "fulltext.mt_doc_target",
            string indexName = "mt_doc_target_idx_fts",
            string regConfig = "english",
            string dataConfig = null)
        {
            var documentMapping = storage.MappingFor(typeof(TDocument));
            var table = new DocumentTable(documentMapping);
            var ddl = documentMapping.Indexes
                .Where(x => x.Name == indexName)
                .Select(x => x.ToDDL(table))
                .FirstOrDefault();

            ddl.ShouldNotBeNull();

            SpecificationExtensions.ShouldContain(ddl, $"CREATE INDEX {indexName}");
            SpecificationExtensions.ShouldContain(ddl, $"ON {tableName}");
            SpecificationExtensions.ShouldContain(ddl, $"to_tsvector('{regConfig}', {dataConfig})");

            if (regConfig != null)
            {
                SpecificationExtensions.ShouldContain(ddl, regConfig);
            }

            if (dataConfig != null)
            {
                SpecificationExtensions.ShouldContain(ddl, dataConfig);
            }
        }
    }
}
