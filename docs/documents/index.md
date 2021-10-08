# Marten as Document DB

Marten's original focus was on enabling Postgresql as a document database for .Net developers
that would allow developers to work very efficiently compared to the typical RDBMS + ORM approach while
delivering solid performance. In Marten's case, persistent .Net _documents_ are just serialized to JSON
and persisted to Postgresql using its unique JSONB capabilities. The advantage to developers of this approach
is that there is much less configuration work necessary to map their persistent classes to a database structure.
Developers are also far more able to evolve their application's model as there is so much less friction in 
changing the persistence layer with Marten compared to the RDBMS + ORM combination.

Here's an introduction to Marten Db as a document database from .Net Conf 2018:

<iframe src="https://channel9.msdn.com/Events/dotnetConf/2018/S315/player" width="960" height="540" allowFullScreen frameBorder="0" title="Marten: Postgresql backed Document Db and Event Store for .NET Development - Microsoft Channel 9 Video"></iframe>


