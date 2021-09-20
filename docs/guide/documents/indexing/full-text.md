# Full Text Indexes

Full Text Indexes in Marten are built based on [GIN or GiST indexes](/guide/documents/configuration/gin-gist-indexes) utilizing [Postgres built in Text Search functions](https://www.postgresql.org/docs/10/textsearch-controls.html). This enables the possibility to do more sophisticated searching through text fields.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L95-L103' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_whole_document_full_text_index_through_store_options_with_default' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L108-L116' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_full_text_index_through_store_options_with_default' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L121-L135' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_full_text_index_through_store_options_with_custom_settings' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L140-L148' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multiple_properties_full_text_index_through_store_options_with_default' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L153-L167' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multiple_properties_full_text_index_through_store_options_with_custom_settings' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L172-L182' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_more_than_one_full_text_index_through_store_options_with_different_reg_config' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L14-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_full_text_index_through_attribute_on_class_with_default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* single property

<!-- snippet: sample_using_a_single_property_full_text_index_through_attribute_with_default -->
<a id='snippet-sample_using_a_single_property_full_text_index_through_attribute_with_default'></a>
```cs
public class UserProfile
{
    public Guid Id { get; set; }

    [FullTextIndex]
    public string Information { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L29-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_full_text_index_through_attribute_with_default' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L40-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_full_text_index_through_attribute_with_custom_settings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

* multiple properties

<!-- snippet: sample_using_multiple_properties_full_text_index_through_attribute_with_default -->
<a id='snippet-sample_using_multiple_properties_full_text_index_through_attribute_with_default'></a>
```cs
public class Article
{
    public Guid Id { get; set; }

    [FullTextIndex]
    public string Heading { get; set; }

    [FullTextIndex]
    public string Text { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L53-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multiple_properties_full_text_index_through_attribute_with_default' title='Start of snippet'>anchor</a></sup>
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

    [FullTextIndex]
    public string EnglishText { get; set; }

    [FullTextIndex(RegConfig = "italian")]
    public string ItalianText { get; set; }

    [FullTextIndex(RegConfig = "french")]
    public string FrenchText { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/full_text_index.cs#L67-L84' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_multiple_properties_full_text_index_through_attribute_with_custom_settings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
