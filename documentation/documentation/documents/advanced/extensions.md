<!--title:Schema Feature Extensions-->

New in Marten 2.4.0 is the ability to add additional features with custom database schema objects that simply plug into Marten's 
<[linkto:documentation/schema/migrations;title=schema management facilities]>. The key abstraction is the `IFeatureSchema` interface shown below:

<[sample:IFeatureSchema]>

Not to worry though, Marten comes with a base class that makes it a bit simpler to build out new features. Here's a very simple
example that defines a custom table with one column:

<[sample:creating-a-fake-schema-feature]>

Now, to actually apply this feature to your Marten applications, use this syntax:

<[sample:adding-schema-feature]>

Do note that when you use the `Add<T>()` syntax, Marten will pass along the current `StoreOptions` to the constructor function if there is a constructor with that signature. Otherwise, it uses the no-arg constructor.

While you *can* directly implement the `ISchemaObject` interface for something Marten doesn't already support, it's probably far easier to just configure one of the existing implementations shown in the following sections.

* `Table`
* `Function`
* `Sequence`


## Table

Postgresql tables can be modeled with the `Table` class as shown in this example from the event store inside of Marten:

<[sample:EventsTable]>


## Function

Postgresql functions can be managed by creating a subclass of the `Function` base class as shown below from the big "append event" function in the event store:

<[sample:AppendEventFunction]>


## Sequence

[Postgresql sequences](https://www.postgresql.org/docs/10/static/sql-createsequence.html) can be managed with this usage:

<[sample:using-sequence]>

