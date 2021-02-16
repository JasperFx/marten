# Including Related Documents

## Join a Single Document

Marten supports the ability to run include queries that execute a `join` SQL query behind the curtains, in order to fetch a referenced document as well as the queried document. Suppose you are querying for a github `Issue` that contains a property `AssigneeId`, which references the Id of the `User` assigned to the Issue. If you wish to fetch the `User` as well in one trip to the database, you can use the `.Include()` method like so:

<<< @/../src/Marten.Testing/Services/Includes/end_to_end_query_with_include_Tests.cs#sample_simple_include

The first parameter of the `Include()` method takes an expression that specifies the document properties on which the join will be done (`AssigneeId` in this case). The second parameter is the expression that will assign the fetched related document to a previously declared variable (`included` in our case). By default, Marten will use an inner join. This means that any `Issue` with no corresponding `User` (or no `AssigneeId`), will not be fetched. If you wish to override this behaviour, you can add as a third parameter the enum `JoinType.LeftOuter`.

## Join Many Documents

If you wish to fetch a list of related documents, you can declare a `List<User>` variable and pass it as the second parameter. The `Include()` method should be appended with `ToList()` or `ToArray()`.

Instead of a List, you could also use a Dictionary with a key type corresponding to the Id type and a value type corresponding to the Document type:

<<< @/../src/Marten.Testing/Services/Includes/end_to_end_query_with_include_Tests.cs#sample_dictionary_include

## Include Multiple Document Types

Marten also allows you to chain multiple `Include()` calls:

<<< @/../src/Marten.Testing/Services/Includes/end_to_end_query_with_include_Tests.cs#sample_multiple_include

## Chaining other Linq Methods

Marten supports chaining other linq methods to allow more complex quries such as:

* `Where()`
* `OrderBy()`
* `OrderByDescending()`

## Asynchronous Support

Marten supports Include within an asynchronous context. The query will be run asynchronously when you append your query with the corresponding Async method, like:

* `ToListAsync()`
* `SingleAsync()`

And so on...

Marten also supports running an Include query within [batched queries](/guide/documents/querying/batched-queries):

<<< @/../src/Marten.Testing/Services/Includes/end_to_end_query_with_include_Tests.cs#sample_batch_include
