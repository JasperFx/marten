# How Documents are Stored

Marten will create a new database table and _upsert_ function for each document type. By default, the table name is
`mt_doc_[alias]` and the function is `mt_upsert_[alias]`, where "alias" is the document type name in all lower case letters, or "parent type name + inner type name" for nested types.

In the not unlikely case that you need to disambiguate table storage for two or more documents with the same type name, you can override the type alias either programmatically with `MartenRegistry`:

<!-- snippet: sample_marten-registry-to-override-document-alias -->
<a id='snippet-sample_marten-registry-to-override-document-alias'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    _.Schema.For<User>().DocumentAlias("folks");
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/configuring_the_document_type_alias_Tests.cs#L23-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten-registry-to-override-document-alias' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

or by decorating the actual document class with an attribute:

<!-- snippet: sample_using-document-alias-attribute -->
<a id='snippet-sample_using-document-alias-attribute'></a>
```cs
[DocumentAlias("johndeere")]
public class Tractor
{
    public string id;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/configuring_the_document_type_alias_Tests.cs#L35-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-document-alias-attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
