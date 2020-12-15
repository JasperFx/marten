## Document Storage Class Diagram

Notes:

* `IUnitOfWork` tracks all the outstanding operations (updates, upserts, deletes, append/create events) for the current `DocumentSessionBase`
* `IDocumentStorage<T>` classes are generated and compiled at runtime by Marten by document type *and* by session type (query only, lightweight, identity map, dirty checking). 
* `IStorageOperation` classes are also generated and compiled at runtime by Marten for document types. This interface effectively represents a single SQL statement to be executed as well as any
postprocessing like optimistic concurrency checks or version assignments
* `IUpdateBatch` is just a helper to execute an array of `IStorageOperation` objects in a single transaction and one or more batched database commands
* `IMartenSession` is an internal interface that's new in Marten V4 that is used to execute query operations. All `QuerySession` and `DocumentSessionBase` classes in Marten also implement `IMartenSession`
* `CommandBuilder` is a utility class to build up SQL statements


```mermaid
classDiagram

class IDocumentSession {
+ SaveChangesAsync()
+ SaveChanges()
}
<<interface>> IDocumentSession

class IStorageOperation {
    <<interface>> IStorageOperation
    Postprocess(DbDataReader, IList~Exception~)
    PostprocessAsync(DbDataReader, IList~Exception~, CancellationToken) 
}


class IQueryHandler {
    + ConfigureCommand(CommandBuilder, IMartenSession)
}
<<interface>> IQueryHandler

IStorageOperation ..|> IQueryHandler

class IUpdateBatch {
    + ApplyChanges(IMartenSession)
    + ApplyChangesAsync(IMartenSession)
}
<<interface>> IUpdateBatch

class CommandBuilder

IUpdateBatch --> CommandBuilder
IQueryHandler --> CommandBuilder

class IMartenSession
<<interface>> IMartenSession

class DocumentSessionBase
<<abstract>> DocumentSessionBase

DocumentSessionBase ..|> IMartenSession
DocumentSessionBase ..|> IDocumentSession

IStorageOperation --> IMartenSession

class IUnitOfWork
<<interface>> IUnitOfWork

IUnitOfWork o-- IStorageOperation
IUpdateBatch o-- IStorageOperation
DocumentSessionBase --> IUpdateBatch
DocumentSessionBase --> IUnitOfWork
IDocumentStorage~T~ --> IStorageOperation
DocumentSessionBase --> IDocumentStorage~T~

class IDocumentStorage~T~
<<interface>> IDocumentStorage~T~

class LightweightDocumentSession

class DirtyCheckingDocumentSession

class IdentityMapDocumentSession

LightweightDocumentSession --|> DocumentSessionBase
DirtyCheckingDocumentSession --|> DocumentSessionBase
IdentityMapDocumentSession --|> DocumentSessionBase

```

## IDocumentSession.SaveChangesAsync() Sequence Diagram

This diagram omits the event appending workflow. That will be shown
in another document.

```mermaid
sequenceDiagram
autoNumber
Client ->> DocumentSessionBase:SaveChangesAsync()
Note right of DocumentSessionBase: The method will exit w/o any outstanding work
DocumentSessionBase->>IUnitOfWork:HasOutstandingWork()
IUnitOfWork-->>DocumentSessionBase:any work to commit?

DocumentSessionBase->>IManagedConnection:BeginTransaction()
Note left of IUnitOfWork: Order the operations between document type dependencies
DocumentSessionBase->>IUnitOfWork: Sort()
loop for each listener
    DocumentSessionBase->>IDocumentSessionListener:BeforeSaveChanges(session)
end

DocumentSessionBase->>IUnitOfWork: AllOperations
IUnitOfWork-->>DocumentSessionBase: sorted IStorageOperation objects
Note right of DocumentSessionBase: Create the batched database commands from the ordered operations in the unit of work
DocumentSessionBase->>UpdateBatch: new(operations)
DocumentSessionBase->>UpdateBatch: ApplyChangesAsync(session)


DocumentSessionBase->>IManagedConnection:CommandAsync()

loop for each listener
    DocumentSessionBase->>IDocumentSessionListener:AfterCommitAsync(session, unitOfWork, token)
end

note right of DocumentSessionBase: finally, reset the internal unit of work

```

## Internals of UpdateBatch

``` mermaid
sequenceDiagram

DocumentSessionBase->>UpdateBatch: ApplyChangesAsync(session)

note right of UpdateBatch: build up the batched database command(s) for the operations
loop for each IStorageOperation
    UpdateBatch->>IStorageOperation: ConfigureCommand(builder, session)
end
note right of UpdateBatch: execute the batched database command (s)
UpdateBatch->>IManagedConnection: ExecuteReaderAsync(command, token)

note right of UpdateBatch: check any post processing conditions (like optimistic version checks) per operation
loop for each IStorageOperation
    UpdateBatch->>IStorageOperation: PostprocessAsync(reader, exceptions, token)
end

```

