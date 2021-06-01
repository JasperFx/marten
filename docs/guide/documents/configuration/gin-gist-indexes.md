# GIN or GiST Indexes

See [Exploring the Postgres GIN index](https://hashrocket.com/blog/posts/exploring-postgres-gin-index) for more information on the GIN index strategy within Postgresqsl.

To optimize a wider range of ad-hoc queries against the document JSONB, you can apply a [GIN index](http://www.postgresql.org/docs/9.4/static/gin.html) to
the JSON field in the database:

<!-- snippet: sample_IndexExamples -->
<!-- endSnippet -->

**Marten may be changed to make the GIN index on the data field be automatic in the future.**
