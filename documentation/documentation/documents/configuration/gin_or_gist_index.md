<!--title: Gin or Gist Indexes-->

See [Exploring the Postgres Gin index](https://hashrocket.com/blog/posts/exploring-postgres-gin-index) for more information on the Gin index strategy within Postgresqsl.

To optimize a wider range of adhoc queries against the document JSONB, you can apply a [Gin index](http://www.postgresql.org/docs/9.4/static/gin.html) to
the JSON field in the database:

<[sample:IndexExamples]>

**Marten may be changed to make the Gin index on the data field be automatic in the future.**

