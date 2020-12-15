## QueryHandler Class Diagram

Notes:

* Every query done through `IQuerySession` first builds up an `IQueryHandler<T>` object that is then used to build up the correct SQL,
then be able to coerce the resulting `DbDataReader` into the desired results
* There are about a dozen implementations of `IQueryHandler<T>`, but most of the Linq queries are done with the very generic `ListQueryHandler` and `OneResultHandler` 
* `ISelector<T>` changed just a little bit in V4
* `ISelectClause` is new for V4. Both `ISelector<T>` and `ISelectClause` classes in V4 are generated at runtime for each document type and flavor of `IDocumentSession` (query only, lightweight, identity map, dirty checked). In all cases, the code generation is building up the most efficient code possible for each permutation by omitting any unnecessary work
* `Statement` was a new concept introduced in V4 that models a single SQL statement. 

``` mermaid
classDiagram


class IQueryHandler~T~ {
    <<interface>> IQueryHandler~T~
    ConfigureCommand(CommandBuilder, IMartenSession)
    Handle(DbDataReader, IMartenSession) T
    HandleAsync(DbDataReader, IMartenSession, CancellationToken) Task~T~
}

class ListQueryHandler
class OneResultHandler

ListQueryHandler --> Statement: Uses
ListQueryHandler --> ISelector: Uses

ListQueryHandler ..|> IQueryHandler
OneResultHandler ..|> IQueryHandler

OneResultHandler --> Statement: Uses
OneResultHandler --> ISelector

class ISelector~T~ {
    <<interface>> ISelector
    Resolve(DbDataReader) T
    ResolveAsync(DbDataReader) Task~T~
}

ISelector --> IMartenSession: Uses
IQueryHandler --> IMartenSession: Uses
Statement --> CommandBuilder: Configures

class CommandBuilder


class IMartenSession {
    <<interface>> IMartenSession

}

class Statement {
    <<abstract>> Statement
    Configure(CommandBuilder)
}

```


## Parsing a Linq Query

Notes:

* Marten heavily uses the Remotion.Linq library for the Linq parsing
* `QueryModel` is part of Remotion.Linq
* `LinqHandlerBuilder` is the entry point for the Linq parsing
* `MartenQueryParser` is mostly unchanged from V3 to V4

``` mermaid
sequenceDiagram

note right of LinqQueryProvider: there are other options to inject result operators
LinqQueryProvider ->> LinqHandlerBuilder: new(IMartenSession, Expression)
LinqHandlerBuilder ->> MartenQueryParser: GetParsedQuery(Expression)
MartenQueryParser -->> LinqHandlerBuilder: QueryModel
LinqHandlerBuilder ->> IMartenSession: StorageFor(Type)
IMartenSession -->> LinqHandlerBuilder: IDocumentStorage
LinqHandlerBuilder ->> DocumentStatement: new(IDocumentStorage)
note right of LinqHandlerBuilder: A *lot* of stuff happens in here to visit all the body, where, order, take, while, and select clauses of the Linq query
LinqHandlerBuilder -> LinqHandlerBuilder: readQueryModel(QueryModel)

```



## Executing a Linq Query

Notes:

* `LinqQueryProvider` and `IMartenQueryable<T>` are plugged into the Remotion.Linq execution model
* `IMartenQueryable<T>` has quite a few extensions on top of the basic Linq `IQueryable<T>` model

``` mermaid
sequenceDiagram

Client->>IQuerySession:Query~T~()
IQuerySession-->>Client: IMartenQueryable~T~
note right of IQuerySession: configure the query through Linq operators
Client->>IMartenQueryable~T~: Where()/Select()/OrderBy()/etc.
note right of IQuerySession: invoke one of the Linq query result operators
Client->>IMartenQueryable~T~: ToListAsync()
IMartenQueryable~T~ ->> LinqQueryProvider:ExecuteAsync(Expression)
LinqQueryProvider ->> LinqHandlerBuilder: new(IMartenSession, Expression)
note right of LinqQueryProvider: This is where all the Linq parsing magic happens
LinqQueryProvider ->> LinqHandlerBuilder: BuildHandler()
LinqHandlerBuilder -->> LinqQueryProvider: IQueryHandler
LinqQueryProvider ->> LinqQueryProvider: ExecuteHandlerAsync(IQueryHandler)
note right of LinqQueryProvider: Build up the database command
LinqQueryProvider ->> IMartenSession: BuildCommand(IQueryHandler)
IMartenSession ->> IQueryHandler: ConfigureCommand(CommandBuilder, IMartenSession)
IMartenSession -->> LinqQueryProvider: NpgsqlCommand
LinqQueryProvider ->> IManagedConnection: ExecuteReaderAsync()
IManagedConnection -->> LinqQueryProvider: DbDataReader
LinqQueryProvider ->> IQueryHandler: HandleAsync(DbDataReader, IMartenSession)
IQueryHandler -->> LinqQueryProvider: results
LinqQueryProvider -->> Client: results
```


## Statement Class Diagram

Notes:

* The `IField` model was introduced in V4 to give us much more flexibility in how different .Net types are represented and used withing the SQL generation from Linq queries. It is possible to plug in
custom `IField` sources to handle special things like F# discriminated unions
* `ISelectClause` writes out the SELECT clause portion of the generated SQL statement. In V4, the `ISelectClause` implementation for each document type is part of the generated `IDocumentStorage<T>` classes generated for each document type + flavor of `IDocumentSession`
* `IFieldMapping` is a collection of `IField` objects for a certain document type. The old `DocumentMapping` class is the main implementation of `IFieldMapping`
* `Statement` is a linked list because in some cases (child queries, complex `Include()` operations, `SelectMany()`), Marten v4 has to use [common table expressions](https://www.postgresqltutorial.com/postgresql-cte/) in queries or even builds out temporary tables.
* `ISqlFragment` was renamed from `IWhereFragment` from < V4 to reflect that it's used more generally in V4 now frequently to represent other bits and pieces of SQL being generated
* `WhereClause` and `Ordering` are part of Remotion.Linq

``` mermaid
classDiagram

class ISelectClause {
    <<interface>> ISelectClause
    WriteSelectClause(CommandBuilder)
    BuildSelector(IMartenSession) ISelector
    BuildHandler~T~(IMartenSession, Statement, Statement) IQueryHandler~T~
}

ISelectClause --> ISelector: Builds
ISelectClause --> CommandBuilder: Configures


class Statement {
    <<abstract>> Statement
    Configure(CommandBuilder)
    ISqlFragment Where
    Offset
    Limit
}

Statement --> Statement : Next
Statement --> CommandBuilder : Writes to
Statement --> IFieldMapping : 1
Statement --> ISqlFragment : Where

class ISqlFragment {
    <<interface>> ISqlFragment
    Apply(CommandBuilder)

}

class IFieldMapping {
    <<interface>> IFieldMapping
}

class IField {
    <<interface>> IField
}

IFieldMapping *-- IField


class WhereClause
class Ordering

Statement --> WhereClause: 0..*
Statement --> Ordering: 0..*

class WhereClauseParser {
    Build(WhereClause) ISqlFragment
}

Statement --> WhereClauseParser: Uses

```

## Statement Sql Writing

``` mermaid
sequenceDiagram
note left of Statement: This flow is for typical Linq query statements
IQueryHandler ->> Statement: Configure(CommandBuilder)
Statement ->> ISelectClause: WriteSelectClause(CommandBuilder)
note right of Statement: for any WHERE clause

Statement ->> ISqlFragment: Apply(CommandBuilder)
Statement ->> Statement: writeOrderClause(CommandBuilder)
note right of Statement: Also writes out any non zero Limit or Offset clauses to the CommandBuilder

alt if there is a next statement
    Statement --> Statement: Configure(CommandBuilder)
end

```