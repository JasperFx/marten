# Marten as Document DB

Marten's original focus was on enabling Postgresql as a document database for .Net developers. In Marten's case, this means
that instead of an ORM like EF Core where you have to map .NET types to flat relational database tables, Marten just utilizes
JSON serialization to persist and load .NET objects ("documents"). In conjunction with PostgreSQL's JSONB data type and its ability
to efficiently support rich querying and even indexing through JSON documents, Marten's approach has turned out to be highly
effective to implement persistence in many .NET applications.

When a document database is a good fit for a system (mostly when you have relatively self-contained entities and don't need to
model complex relationships between document types), Marten can make teams much more productive over ORM or purely relational
database usage by:

* Eliminating explicit ORM mapping
* Being able to accept changes as entities evolve without having to worry much about database migrations
* Utilizing built in database initialization and migrations at runtime so you can "just work" 

Here's an introduction to Marten Db as a document database from .Net Conf 2018:

<iframe src="https://channel9.msdn.com/Events/dotnetConf/2018/S315/player" width="960" height="540" allowFullScreen frameBorder="0" title="Marten: Postgresql backed Document Db and Event Store for .NET Development - Microsoft Channel 9 Video"></iframe>
