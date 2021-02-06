<!--Title: Storing & reading back non-uniform JSON documents via dynamic -->

This scenario demonstrates how to store and query non-uniform documents via the help of [`dynamic`](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/types/using-type-dynamic).

## Scenario

Let us assume we have a document with non-uniform records, presenting temperature sensor data whereby individual records are identified either via the field _detector_ or _sensor_.

<[sample:sample-scenarios-dynamic-records]>

To store and later read back these records, we create a wrapper type with a `dynamic` property to present our record.

<[sample:sample-scenarios-dynamic-type]>

We then read and serialize our records into our newly introduced intermediate type and persist an array of its instances via `BulkInsert`. Lastly, we read back the records, via the non-generic `Query` extension method, passing in predicates that take into account the non-uniform fields of the source documents. After reading back the data for _sensor-1_, we calculate the average of its recorded temperatures:

<[sample:sample-scenarios-dynamic-insertandquery]>
