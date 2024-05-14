# Indexing Documents

::: warning
In all recent versions, Marten owns all the indexes on Marten controlled tables, so any custom indexes needs to be done
through Marten itself, or you need to bypass Marten's own facilities for schema management to avoid having Marten drop
your custom indexes.
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
