using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Indexes;
using Xunit;

namespace DocumentDbTests.Indexes;

#region sample_using_a_full_text_index_through_attribute_on_class_with_default

[FullTextIndex]
public class Book
{
    public Guid Id { get; set; }

    public string Title { get; set; }

    public string Author { get; set; }

    public string Information { get; set; }
}

#endregion

#region sample_using_a_single_property_full_text_index_through_attribute_with_default

public class UserProfile
{
    public Guid Id { get; set; }

    [FullTextIndex] public string Information { get; set; }
}

#endregion

#region sample_using_a_single_property_full_text_index_through_attribute_with_custom_settings

public class UserDetails
{
    private const string FullTextIndexName = "mt_custom_user_details_fts_idx";

    public Guid Id { get; set; }

    [FullTextIndex(IndexName = FullTextIndexName, RegConfig = "italian")]
    public string Details { get; set; }
}

#endregion

#region sample_using_multiple_properties_full_text_index_through_attribute_with_default

public class Article
{
    public Guid Id { get; set; }

    [FullTextIndex] public string Heading { get; set; }

    [FullTextIndex] public string Text { get; set; }
}

#endregion

#region sample_using_multiple_properties_full_text_index_through_attribute_with_custom_settings

public class BlogPost
{
    public Guid Id { get; set; }

    public string Category { get; set; }

    [FullTextIndex] public string EnglishText { get; set; }

    [FullTextIndex(RegConfig = "italian")] public string ItalianText { get; set; }

    [FullTextIndex(RegConfig = "french")] public string FrenchText { get; set; }
}

#endregion

public class full_text_index: OneOffConfigurationsContext
{
    public void using_whole_document_full_text_index_through_store_options_with_default()
    {
        #region sample_using_whole_document_full_text_index_through_store_options_with_default

        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);

            // This creates
            _.Schema.For<User>().FullTextIndex();
        });

        #endregion
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

        #endregion
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

        #endregion
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

        #endregion
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

        #endregion
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

        #endregion
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task using_full_text_query_through_query_session()
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

        using (var session = store.LightweightSession())
        {
            session.Store(new User { FirstName = "Jeremy", LastName = "Miller", UserName = "jmiller" });
            session.Store(new User { FirstName = "Lindsey", LastName = "Miller", UserName = "lmiller" });
            session.Store(new User { FirstName = "Max", LastName = "Miller", UserName = "mmiller" });
            session.Store(new User { FirstName = "Frank", LastName = "Zombo", UserName = "fzombo" });
            session.Store(new User { FirstName = "Somebody", LastName = "Somewher", UserName = "somebody" });
            await session.SaveChangesAsync();

            result = await session.SearchAsync<User>("somebody");
        }

        store.Dispose();

        #endregion

        result.Count().ShouldBe(1);
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task search_in_query_sample()
    {
        StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

        var expectedId = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter" });
            session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter" });
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            #region sample_search_in_query_sample

            var posts = session.Query<BlogPost>()
                .Where(x => x.Search("somefilter"))
                .ToList();

            #endregion

            posts.Count.ShouldBe(1);
            posts.Single().Id.ShouldBe(expectedId);
        }
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task plain_text_search_in_query_sample()
    {
        StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

        var expectedId = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter" });
            session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter" });
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            #region sample_plain_search_in_query_sample

            var posts = session.Query<BlogPost>()
                .Where(x => x.PlainTextSearch("somefilter"))
                .ToList();

            #endregion

            posts.Count.ShouldBe(1);
            posts.Single().Id.ShouldBe(expectedId);
        }
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task phrase_search_in_query_sample()
    {
        StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

        var expectedId = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter" });
            session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter" });
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            #region sample_phrase_search_in_query_sample

            var posts = session.Query<BlogPost>()
                .Where(x => x.PhraseSearch("somefilter"))
                .ToList();

            #endregion

            posts.Count.ShouldBe(1);
            posts.Single().Id.ShouldBe(expectedId);
        }
    }

    [PgVersionTargetedFact(MinimumVersion = "11.0")]
    public async Task web_search_in_query_sample()
    {
        StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

        var expectedId = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter" });
            session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter" });
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            #region sample_web_search_in_query_sample

            var posts = session.Query<BlogPost>()
                .Where(x => x.WebStyleSearch("somefilter"))
                .ToList();

            #endregion

            posts.Count.ShouldBe(1);
            posts.Single().Id.ShouldBe(expectedId);
        }
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task text_search_combined_with_other_query_sample()
    {
        StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

        var expectedId = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Store(new BlogPost { Id = expectedId, EnglishText = "somefilter", Category = "LifeStyle" });
            session.Store(new BlogPost { Id = Guid.NewGuid(), EnglishText = "somefilter", Category = "Other" });
            session.Store(new BlogPost { Id = Guid.NewGuid(), ItalianText = "somefilter", Category = "LifeStyle" });
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            #region sample_text_search_combined_with_other_query_sample

            var posts = session.Query<BlogPost>()
                .Where(x => x.Category == "LifeStyle")
                .Where(x => x.PhraseSearch("somefilter"))
                .ToList();

            #endregion

            posts.Count.ShouldBe(1);
            posts.Single().Id.ShouldBe(expectedId);
        }
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task text_search_with_non_default_regConfig_sample()
    {
        StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

        var expectedId = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Store(new BlogPost { Id = Guid.NewGuid(), EnglishText = "somefilter" });
            session.Store(new BlogPost { Id = expectedId, ItalianText = "somefilter" });
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            #region sample_text_search_with_non_default_regConfig_sample

            var posts = session.Query<BlogPost>()
                .Where(x => x.PhraseSearch("somefilter", "italian"))
                .ToList();

            #endregion

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
    public async Task should_search_with_store_options_with_multipleIndexes()
    {
        const string frenchRegConfig = "french";
        const string italianRegConfig = "italian";

        StoreOptions(_ => _.Schema.For<User>()
            .FullTextIndex(italianRegConfig, d => d.FirstName)
            .FullTextIndex(frenchRegConfig, d => d.LastName));

        const string searchFilter = "Lindsey";

        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = searchFilter, LastName = "Miller", UserName = "lmiller" });
        session.Store(new User { FirstName = "Frank", LastName = searchFilter, UserName = "fzombo" });

        session.Store(new User { FirstName = "Jeremy", LastName = "Miller", UserName = "jmiller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller", UserName = "mmiller" });
        session.Store(new User { FirstName = "Somebody", LastName = "Somewher", UserName = "somebody" });
        await session.SaveChangesAsync();

        var italianResults = await session.SearchAsync<User>(searchFilter, italianRegConfig);

        italianResults.Count.ShouldBe(1);
        italianResults.ShouldContain(u => u.FirstName == searchFilter);
        italianResults.ShouldNotContain(u => u.LastName == searchFilter);

        var frenchResults = await session.SearchAsync<User>(searchFilter, frenchRegConfig);

        frenchResults.Count.ShouldBe(1);
        frenchResults.ShouldNotContain(u => u.FirstName == searchFilter);
        frenchResults.ShouldContain(u => u.LastName == searchFilter);
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task should_search_by_tenant_with_tenancy_conjoined()
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
            using var session = theStore.LightweightSession(tenant);
            session.Store(new User { FirstName = searchFilter, LastName = "Miller", UserName = "lmiller" });
            session.Store(new User { FirstName = "Frank", LastName = "Zombo", UserName = "fzombo" });
            await session.SaveChangesAsync();
        }

        foreach (var tenant in tenants)
        {
            using var session = theStore.QuerySession(tenant);
            var results = await session.SearchAsync<User>(searchFilter);

            results.Count.ShouldBe(1);
            results.ShouldContain(u => u.FirstName == searchFilter);
            results.ShouldNotContain(u => u.LastName == searchFilter);
        }
    }

    private async Task SearchShouldBeSuccessfulFor(Action<StoreOptions> configure)
    {
        StoreOptions(configure);

        const string searchFilter = "Lindsey";

        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = searchFilter, LastName = "Miller", UserName = "lmiller" });
        session.Store(new User { FirstName = "Frank", LastName = searchFilter, UserName = "fzombo" });

        session.Store(new User { FirstName = "Jeremy", LastName = "Miller", UserName = "jmiller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller", UserName = "mmiller" });
        session.Store(new User { FirstName = "Somebody", LastName = "Somewher", UserName = "somebody" });
        await session.SaveChangesAsync();

        var results = await session.SearchAsync<User>(searchFilter);

        results.Count.ShouldBe(2);
        results.ShouldContain(u => u.FirstName == searchFilter);
        results.ShouldContain(u => u.LastName == searchFilter);
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

        var table = await theStore.ExistingTableFor(typeof(Target));
        var index = table.IndexFor("mt_doc_target_idx_fts");
        index.ShouldNotBeNull();

        index.ToDDL(table).ShouldContain("to_tsvector");
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task
        creating_a_full_text_index_with_custom_data_configuration_should_create_the_index_without_regConfig_in_indexname_custom_data_configuration()
    {
        const string dataConfig = "(data ->> 'AnotherString' || ' ' || 'test')";

        StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
            index =>
            {
                index.DocumentConfig = dataConfig;
            }));

        var data = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(data);

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Target>(
                indexName: $"mt_doc_target_idx_fts",
                dataConfig: dataConfig
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task
        creating_a_full_text_index_with_custom_data_configuration_and_custom_regConfig_should_create_the_index_with_custom_regConfig_in_indexname_custom_data_configuration()
    {
        const string dataConfig = "(data ->> 'AnotherString' || ' ' || 'test')";
        const string regConfig = "french";

        StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
            index =>
            {
                index.RegConfig = regConfig;
                index.DocumentConfig = dataConfig;
            }));

        var data = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(data);

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Target>(
                indexName: $"mt_doc_target_{regConfig}_idx_fts",
                regConfig: regConfig,
                dataConfig: dataConfig
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task
        creating_a_full_text_index_with_custom_data_configuration_and_custom_regConfig_custom_indexName_should_create_the_index_with_custom_indexname_custom_data_configuration()
    {
        const string dataConfig = "(data ->> 'AnotherString' || ' ' || 'test')";
        const string regConfig = "french";
        const string indexName = "custom_index_name";

        StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
            index =>
            {
                index.DocumentConfig = dataConfig;
                index.RegConfig = regConfig;
                index.Name = indexName;
            }));

        var data = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(data);

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Target>(
                indexName: indexName,
                regConfig: regConfig,
                dataConfig: dataConfig
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task
        creating_a_full_text_index_with_single_member_should_create_the_index_without_regConfig_in_indexname_and_member_selectors()
    {
        StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(d => d.String));

        var data = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(data);

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Target>(
                indexName: $"mt_doc_target_idx_fts",
                dataConfig: $"((data ->> '{nameof(Target.String)}'))"
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task
        creating_a_full_text_index_with_multiple_members_should_create_the_index_without_regConfig_in_indexname_and_members_selectors()
    {
        StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(d => d.String, d => d.AnotherString));

        var data = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(data);

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Target>(
                indexName: $"mt_doc_target_idx_fts",
                dataConfig:
                $"((data ->> '{nameof(Target.String)}') || ' ' || (data ->> '{nameof(Target.AnotherString)}'))"
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task
        creating_a_full_text_index_with_multiple_members_and_custom_configuration_should_create_the_index_with_custom_configuration_and_members_selectors()
    {
        const string indexName = "custom_index_name";
        const string regConfig = "french";

        StoreOptions(_ => _.Schema.For<Target>().FullTextIndex(
            index =>
            {
                index.Name = indexName;
                index.RegConfig = regConfig;
            },
            d => d.AnotherString));

        var data = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(data);

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Target>(
                indexName: indexName,
                regConfig: regConfig,
                dataConfig: $"((data ->> '{nameof(Target.AnotherString)}'))"
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task
        creating_multiple_full_text_index_with_different_regConfigs_and_custom_data_config_should_create_the_indexes_with_different_recConfigs()
    {
        const string frenchRegConfig = "french";
        const string italianRegConfig = "italian";

        StoreOptions(_ => _.Schema.For<Target>()
            .FullTextIndex(frenchRegConfig, d => d.String)
            .FullTextIndex(italianRegConfig, d => d.AnotherString));

        var data = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(data);

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Target>(
                indexName: $"mt_doc_target_{frenchRegConfig}_idx_fts",
                regConfig: frenchRegConfig,
                dataConfig: $"((data ->> '{nameof(Target.String)}'))"
            );

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Target>(
                indexName: $"mt_doc_target_{italianRegConfig}_idx_fts",
                regConfig: italianRegConfig,
                dataConfig: $"((data ->> '{nameof(Target.AnotherString)}'))"
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task using_a_full_text_index_through_attribute_on_class_with_default()
    {
        StoreOptions(_ => _.RegisterDocumentType<Book>());

        await theStore.BulkInsertAsync(new[]
        {
            new Book { Id = Guid.NewGuid(), Author = "test", Information = "test", Title = "test" }
        });

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Book>(
                tableName: "full_text_index.mt_doc_book",
                indexName: $"mt_doc_book_idx_fts",
                regConfig: FullTextIndexDefinition.DefaultRegConfig,
                dataConfig: $"data"
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task using_a_single_property_full_text_index_through_attribute_with_default()
    {
        StoreOptions(_ => _.RegisterDocumentType<UserProfile>());

        await theStore.BulkInsertAsync(new[] { new UserProfile { Id = Guid.NewGuid(), Information = "test" } });

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<UserProfile>(
                tableName: "full_text_index.mt_doc_userprofile",
                indexName: $"mt_doc_userprofile_idx_fts",
                regConfig: FullTextIndexDefinition.DefaultRegConfig,
                dataConfig: $"((data ->> '{nameof(UserProfile.Information)}'))"
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task using_a_single_property_full_text_index_through_attribute_with_custom_settings()
    {
        StoreOptions(_ => _.RegisterDocumentType<UserDetails>());

        await theStore.BulkInsertAsync(new[] { new UserDetails { Id = Guid.NewGuid(), Details = "test" } });

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<UserDetails>(
                tableName: "full_text_index.mt_doc_userdetails",
                indexName: "mt_custom_user_details_fts_idx",
                regConfig: "italian",
                dataConfig: $"((data ->> '{nameof(UserDetails.Details)}'))"
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task using_multiple_properties_full_text_index_through_attribute_with_default()
    {
        StoreOptions(_ => _.RegisterDocumentType<Article>());

        await theStore.BulkInsertAsync(new[] { new Article { Id = Guid.NewGuid(), Heading = "test", Text = "test" } });

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<Article>(
                tableName: "full_text_index.mt_doc_article",
                indexName: $"mt_doc_article_idx_fts",
                regConfig: FullTextIndexDefinition.DefaultRegConfig,
                dataConfig: $"((data ->> '{nameof(Article.Heading)}') || ' ' || (data ->> '{nameof(Article.Text)}'))"
            );
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task using_multiple_properties_full_text_index_through_attribute_with_custom_settings()
    {
        const string frenchRegConfig = "french";
        const string italianRegConfig = "italian";

        StoreOptions(_ => _.RegisterDocumentType<BlogPost>());

        await theStore.BulkInsertAsync(new[]
        {
            new BlogPost
            {
                Id = Guid.NewGuid(),
                Category = "test",
                EnglishText = "test",
                FrenchText = "test",
                ItalianText = "test"
            }
        });

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<BlogPost>(
                tableName: "full_text_index.mt_doc_blogpost",
                indexName: $"mt_doc_blogpost_idx_fts",
                regConfig: FullTextIndexDefinition.DefaultRegConfig,
                dataConfig: $"((data ->> '{nameof(BlogPost.EnglishText)}'))"
            );

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<BlogPost>(
                tableName: "full_text_index.mt_doc_blogpost",
                indexName: $"mt_doc_blogpost_{frenchRegConfig}_idx_fts",
                regConfig: frenchRegConfig,
                dataConfig: $"((data ->> '{nameof(BlogPost.FrenchText)}'))"
            );

        theStore.StorageFeatures
            .ShouldContainIndexDefinitionFor<BlogPost>(
                tableName: "full_text_index.mt_doc_blogpost",
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
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Look at updates after that
        var patch = await theStore.Storage.Database.CreateMigrationAsync();

        var patchSql = patch.UpdateSql();

        Assert.DoesNotContain("drop index if exists full_text_index.mt_doc_user_idx_fts", patchSql);
        Assert.DoesNotContain("drop index full_text_index.mt_doc_user_idx_fts", patchSql);
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
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Look at updates after that
        var patch = await theStore.Storage.Database.CreateMigrationAsync();

        var patchSql = patch.UpdateSql();

        Assert.DoesNotContain("drop index if exists full_text_index.mt_doc_user_idx_fts", patchSql);
        Assert.DoesNotContain("drop index full_text_index.mt_doc_user_idx_fts", patchSql);
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
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Look at updates after that
        var patch = await theStore.Storage.Database.CreateMigrationAsync();

        var patchSql = patch.UpdateSql();

        Assert.DoesNotContain("drop index if exists full_text_index.mt_doc_user_idx_fts", patchSql);
        Assert.DoesNotContain("drop index full_text_index.mt_doc_user_idx_fts", patchSql);
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
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Change indexed fields
        StoreOptions(_ =>
        {
            _.Schema.For<User>()
                .FullTextIndex(x => x.FirstName, x => x.LastName);
        }, false);

        // Look at updates after that
        var patch = await theStore.Storage.Database.CreateMigrationAsync();

        Assert.Contains("drop index if exists full_text_index.mt_doc_user_idx_fts", patch.UpdateSql());
    }

    [PgVersionTargetedFact(MinimumVersion = "10.0")]
    public async Task migration_from_v3_to_v4_should_not_result_in_schema_difference()
    {
        // setup/simulate a full text index as in v3
        StoreOptions(_ =>
        {
            _.Schema.For<User>().FullTextIndex();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // drop and recreate index with a sql statement not containing `::regconfig`
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand("DROP INDEX if exists full_text_index.mt_doc_user_idx_fts")
                .ExecuteNonQueryAsync();
            await conn.CreateCommand(
                    "CREATE INDEX mt_doc_user_idx_fts ON full_text_index.mt_doc_user USING gin (( to_tsvector('english', data) ))")
                .ExecuteNonQueryAsync();
        }

        // create another store and check if there is no schema difference
        var store2 = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
            _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            _.DatabaseSchemaName = "fulltext";

            _.Schema.For<User>().FullTextIndex();
        });
        await Should.NotThrowAsync(async () => await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }
}

public static class FullTextIndexTestsExtension
{
    public static void ShouldContainIndexDefinitionFor<TDocument>(
        this StorageFeatures storage,
        string tableName = "full_text_index.mt_doc_target",
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

        ddl.ShouldContain($"CREATE INDEX {indexName}");
        ddl.ShouldContain($"ON {tableName}");
        ddl.ShouldContain($"to_tsvector('{regConfig}',{dataConfig})");

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
