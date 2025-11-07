# Full Text Searching

Full Text Indexes in Marten are built based on [GIN or GiST indexes](/documents/indexing/gin-gist-indexes) utilizing [Postgres built in Text Search functions](https://www.postgresql.org/docs/10/textsearch-controls.html). This enables the possibility to do more sophisticated searching through text fields.

::: warning
To use this feature, you will need to use PostgreSQL version 10.0 or above, as this is the first version that support text search function on jsonb column - this is also the data type that Marten use to store it's data.
:::

## Defining Full Text Index through Store options

Full Text Indexes can be created using the fluent interface of `StoreOptions` like this:

* one index for whole document - all document properties values will be indexed

<!-- snippet: sample_using_whole_document_full_text_index_through_store_options_with_default -->
<a id='snippet-sample_using_whole_document_full_text_index_through_store_options_with_default'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    // This creates
    _.Schema.For<User>().FullTextIndex();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L96-L106' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_whole_document_full_text_index_through_store_options_with_default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
If you don't specify language (regConfig) - by default it will be created with 'english' value.
:::

* single property - there is possibility to specify specific property to be indexed

<!-- snippet: sample_using_a_single_property_full_text_index_through_store_options_with_default -->
<a id='snippet-sample_using_a_single_property_full_text_index_through_store_options_with_default'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    // This creates
    _.Schema.For<User>().FullTextIndex(d => d.FirstName);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L111-L121' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_full_text_index_through_store_options_with_default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* single property with custom settings

<!-- snippet: sample_using_a_single_property_full_text_index_through_store_options_with_custom_settings -->
<a id='snippet-sample_using_a_single_property_full_text_index_through_store_options_with_custom_settings'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L126-L142' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_full_text_index_through_store_options_with_custom_settings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* multiple properties

<!-- snippet: sample_using_multiple_properties_full_text_index_through_store_options_with_default -->
<a id='snippet-sample_using_multiple_properties_full_text_index_through_store_options_with_default'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    // This creates
    _.Schema.For<User>().FullTextIndex(d => d.FirstName, d => d.LastName);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L147-L157' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multiple_properties_full_text_index_through_store_options_with_default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* multiple properties with custom settings

<!-- snippet: sample_using_multiple_properties_full_text_index_through_store_options_with_custom_settings -->
<a id='snippet-sample_using_multiple_properties_full_text_index_through_store_options_with_custom_settings'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L162-L178' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multiple_properties_full_text_index_through_store_options_with_custom_settings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* more than one index for document with different languages (regConfig)

<!-- snippet: sample_using_more_than_one_full_text_index_through_store_options_with_different_reg_config -->
<a id='snippet-sample_using_more_than_one_full_text_index_through_store_options_with_different_reg_config'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    // This creates
    _.Schema.For<User>()
        .FullTextIndex(d => d.FirstName) //by default it will use "english"
        .FullTextIndex("italian", d => d.LastName);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L183-L195' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_more_than_one_full_text_index_through_store_options_with_different_reg_config' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Defining Full Text  Index through Attribute

Full Text  Indexes can be created using the `[FullTextIndex]` attribute like this:

* one index for whole document - by setting attribute on the class all document properties values will be indexed

<!-- snippet: sample_using_a_full_text_index_through_attribute_on_class_with_default -->
<a id='snippet-sample_using_a_full_text_index_through_attribute_on_class_with_default'></a>
```cs
[FullTextIndex]
public class Book
{
    public Guid Id { get; set; }

    public string Title { get; set; }

    public string Author { get; set; }

    public string Information { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L21-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_full_text_index_through_attribute_on_class_with_default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* single property

<!-- snippet: sample_using_a_single_property_full_text_index_through_attribute_with_default -->
<a id='snippet-sample_using_a_single_property_full_text_index_through_attribute_with_default'></a>
```cs
public class UserProfile
{
    public Guid Id { get; set; }

    [FullTextIndex] public string Information { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L37-L46' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_full_text_index_through_attribute_with_default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
If you don't specify regConfig - by default it will be created with 'english' value.
:::

* single property with custom settings

<!-- snippet: sample_using_a_single_property_full_text_index_through_attribute_with_custom_settings -->
<a id='snippet-sample_using_a_single_property_full_text_index_through_attribute_with_custom_settings'></a>
```cs
public class UserDetails
{
    private const string FullTextIndexName = "mt_custom_user_details_fts_idx";

    public Guid Id { get; set; }

    [FullTextIndex(IndexName = FullTextIndexName, RegConfig = "italian")]
    public string Details { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L48-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_full_text_index_through_attribute_with_custom_settings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* multiple properties

<!-- snippet: sample_using_multiple_properties_full_text_index_through_attribute_with_default -->
<a id='snippet-sample_using_multiple_properties_full_text_index_through_attribute_with_default'></a>
```cs
public class Article
{
    public Guid Id { get; set; }

    [FullTextIndex] public string Heading { get; set; }

    [FullTextIndex] public string Text { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L62-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multiple_properties_full_text_index_through_attribute_with_default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
To group multiple properties into single index you need to specify the same values in `IndexName` parameters.
:::

* multiple indexes for multiple properties with custom settings

<!-- snippet: sample_using_multiple_properties_full_text_index_through_attribute_with_custom_settings -->
<a id='snippet-sample_using_multiple_properties_full_text_index_through_attribute_with_custom_settings'></a>
```cs
public class BlogPost
{
    public Guid Id { get; set; }

    public string Category { get; set; }

    [FullTextIndex] public string EnglishText { get; set; }

    [FullTextIndex(RegConfig = "italian")] public string ItalianText { get; set; }

    [FullTextIndex(RegConfig = "french")] public string FrenchText { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L75-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multiple_properties_full_text_index_through_attribute_with_custom_settings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Text Search

Postgres contains built in [Text Search functions](https://www.postgresql.org/docs/10/textsearch-controls.html). They enable the possibility to do more sophisticated searching through text fields. Marten gives possibility to define (full text indexes)(/documents/configuration/full_text) and perform queries on them.
Currently four types of full Text Search functions are supported:

* regular Search (to_tsquery)

<!-- snippet: sample_search_in_query_sample -->
<a id='snippet-sample_search_in_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.Search("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L249-L255' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_search_in_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* plain text Search (plainto_tsquery)

<!-- snippet: sample_plain_search_in_query_sample -->
<a id='snippet-sample_plain_search_in_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.PlainTextSearch("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L278-L284' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_plain_search_in_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* phrase Search (phraseto_tsquery)

<!-- snippet: sample_phrase_search_in_query_sample -->
<a id='snippet-sample_phrase_search_in_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.PhraseSearch("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L307-L313' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_phrase_search_in_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* web-style Search (websearch_to_tsquery, [supported from Postgres 11+](https://www.postgresql.org/docs/11/textsearch-controls.html)

<!-- snippet: sample_web_search_in_query_sample -->
<a id='snippet-sample_web_search_in_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.WebStyleSearch("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L336-L342' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_web_search_in_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

All types of Text Searches can be combined with other Linq queries

<!-- snippet: sample_text_search_combined_with_other_query_sample -->
<a id='snippet-sample_text_search_combined_with_other_query_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.Category == "LifeStyle")
    .Where(x => x.PhraseSearch("somefilter"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L366-L373' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_text_search_combined_with_other_query_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

They allow also to specify language (regConfig) of the text search query (by default `english` is being used)

<!-- snippet: sample_text_search_with_non_default_regConfig_sample -->
<a id='snippet-sample_text_search_with_non_default_regconfig_sample'></a>
```cs
var posts = session.Query<BlogPost>()
    .Where(x => x.PhraseSearch("somefilter", "italian"))
    .ToList();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/full_text_index.cs#L396-L402' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_text_search_with_non_default_regconfig_sample' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Partial text search in a multi-word text (NGram search)

Marten provides the ability to search partial text or words in a string containing multiple words using NGram search. This is quite similar in functionality to [NGrams in Elastic Search](https://www.elastic.co/guide/en/elasticsearch/reference/current/analysis-ngram-tokenizer.html). As an example, we can now accurately match `rich com text` within `Communicating Across Contexts (Enriched)`. NGram search uses English by default. NGram search also encompasses and handles unigrams, bigrams and trigrams. This functionality is added in v5.

<!-- snippet: sample_ngram_search -->
<a id='snippet-sample_ngram_search'></a>
```cs
var result = await session
    .Query<User>()
    .Where(x => x.UserName.NgramSearch(term))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/NgramSearchTests.cs#L67-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ngram_search' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_ngram_search-1'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(Marten.Testing.Harness.ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "ngram_test";

    // This creates an ngram index for efficient sub string based matching
    _.Schema.For<User>().NgramIndex(x => x.UserName);
});

await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

await using var session = store.LightweightSession();

string term = null;
for (var i = 1; i < 4; i++)
{
    var guid = $"{Guid.NewGuid():N}";
    term ??= guid.Substring(5);

    var newUser = new User(i, $"Test user {guid}");

    session.Store(newUser);
}

await session.SaveChangesAsync();

var result = await session
    .Query<User>()
    .Where(x => x.UserName.NgramSearch(term))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/NgramSearchTests.cs#L82-L113' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ngram_search-1' title='Start of snippet'>anchor</a></sup>
<a id='snippet-sample_ngram_search-2'></a>
```cs
var result = await session
    .Query<User>()
    .Where(x => x.Address.Line1.NgramSearch(term))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/NgramSearchTests.cs#L147-L152' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ngram_search-2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## NGram search on non-English text <Badge type="tip" text="7.39.5" />

If you want to use NGram search on non-English text, Marten provides a mechanism via an opt-in `storeOptions.Advanced.UseNGramSearchWithUnaccent = true` which uses [Postgres unaccent extension](https://www.postgresql.org/docs/current/unaccent.html) for applying before creating ngrams and on search input for a better multilingual experience. Check the sample code below:

<!-- snippet: sample_ngram_search_unaccent -->
<a id='snippet-sample_ngram_search_unaccent'></a>
```cs
var store = DocumentStore.For(_ =>
{
   _.Connection(Marten.Testing.Harness.ConnectionSource.ConnectionString);
   _.DatabaseSchemaName = "ngram_test";
   _.Schema.For<User>().NgramIndex(x => x.UserName);
   _.Advanced.UseNGramSearchWithUnaccent = true;
});

await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

await using var session = store.LightweightSession();
//The ngram uðmu should only exist in bjork, if special characters ignored it will return Umut
var umut = new User(1, "Umut Aral");
var bjork = new User(2, "Björk Guðmundsdóttir");

//The ngram øre should only exist in bjork, if special characters ignored it will return Chris Rea
var kierkegaard = new User(3, "Søren Kierkegaard");
var rea = new User(4, "Chris Rea");

session.Store(umut);
session.Store(bjork);
session.Store(kierkegaard);
session.Store(rea);

await session.SaveChangesAsync();

var result = await session
   .Query<User>()
   .Where(x => x.UserName.NgramSearch("uðmu") || x.UserName.NgramSearch("øre"))
   .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Indexes/NgramSearchTests.cs#L161-L193' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ngram_search_unaccent' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## NGram Search Across Multiple Properties <Badge type="tip" text="7.39.5" />

In many cases, you may want to perform partial text search across multiple fields like `UserName`, `FirstName`, and `LastName`.  

A naive approach might be to apply individual Ngram indexes and search each field separately:

::: danger Don't do this
This results in multiple indexes per document and requires complex `LINQ` queries to combine the results — inefficient and hard to maintain.
:::
```csharp
// Inefficient and verbose
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    // Too many indexes
    _.Schema.For<User>().NgramIndex(d => d.UserName);
    _.Schema.For<User>().NgramIndex(d => d.FirstName);
    _.Schema.For<User>().NgramIndex(d => d.LastName);
});

var result = await session
    .Query<User>()
    .Where(x => x.UserName.NgramSearch(term) 
             || x.FirstName.NgramSearch(term) 
             || x.LastName.NgramSearch(term))
    .ToListAsync();
```
Instead, define a computed property that concatenates the values into a single field, and index that:
```csharp
public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }

    // Combine searchable fields
    public string SearchString => $"{UserName} {FirstName} {LastName}";
}
```
Then configure the Ngram index on that property:

```csharp
    _.Schema.For<User>().NgramIndex(x => x.SearchString);
```
This simplifies querying:
```csharp
var result = await session
    .Query<User>()
    .Where(x => x.SearchString.NgramSearch(term))
    .ToListAsync();
```
