# Case Sensitivity

String comparisons in PostgreSQL are case sensitive* (unless a case-insensitive collation were to be introduced). To work around this, PostgreSQL has several methods to match strings in a case-insensitive manner. Consequently, Marten also makes use of these methods to translate case-insensitive queries to pgSQL. See [querying document with Linq](/guide/documents/querying/linq) for querying by strings.

::: tip INFO
Databases, tables, fields and column names are case-independent, unless created with double quotes.
:::

## Case-Insensitivity & Marten Internals

Marten query parser recognizes case-insensitive comparisons from the use of `StringComparison.CurrentCultureIgnoreCase`. Such comparisons are translated to use the `ILIKE` (or its equivalent operator `~~*`) PostgreSQL extension that matches strings independent of case.

The use of `ILIKE` pattern match in place of equality comparison has the consequence that matching on *%* wildcard literal needs to be escaped as *\\%*, e.g. *abc%* would match on *abc* followed by any characters, whereas *abc\\%* would only match the exact string of *abc%*.

See [Postgresql documentation on pattern matching](https://www.postgresql.org/docs/current/static/functions-matching.html) for more.
