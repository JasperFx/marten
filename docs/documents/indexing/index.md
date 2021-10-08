# Indexing Documents

::: tip
If you intend to write your own indexes against Marten document tables, just ensure that the index names are **not** prefixed with "mt_" so
that Marten will ignore your manual indexes when calculating schema differences.
:::

Marten gives you a couple options for speeding up queries --
which all come at the cost of slower inserts because it's an imperfect world. Marten supports the ability to configure:

* Indexes on the JSONB data field itself
* Duplicate properties into separate database fields with a matching index for optimized querying
* Choose how Postgresql will search within JSONB documents
* DDL generation rules
* How documents will be deleted

My own personal bias is to avoid adding persistence concerns directly to the document types, but other developers
will prefer to use either attributes or the new embedded configuration option with the thinking that it's
better to keep the persistence configuration on the document type itself for easier traceability. Either way,
Marten has you covered with the various configuration options shown here.

