<!--Title:FAQ & Troubleshooting-->

**How do I serialize to Camel case?**

While it's possible to accommodate any serialization schemes by implementing a custom `ISerializer`, Marten's built-in serializer (Json.Net) can be set to serialize to Camel case through `StoreOptions.UseDefaultSerialization`:

<[sample:sample-serialize-to-camelcase]> 	

**How do I disable PLV8?**

If you don't want PLV8 (required for Javascript transformations) related items in your database schema, you can disable PLV8 alltogether by setting `StoreOptions.PLV8Enabled` to false.

