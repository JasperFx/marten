# How Documents are Stored

Marten will create a new database table and _upsert_ function for each document type. By default, the table name is
`mt_doc_[alias]` and the function is `mt_upsert_[alias]`, where "alias" is the document type name in all lower case letters, or "parent type name + inner type name" for nested types.

In the not unlikely case that you need to disambiguate table storage for two or more documents with the same type name, you can override the type alias either programmatically with `MartenRegistry`:

<!-- snippet: sample_marten-registry-to-override-document-alias -->
<!-- endSnippet -->

or by decorating the actual document class with an attribute:

<!-- snippet: sample_using-document-alias-attribute -->
<!-- endSnippet -->
