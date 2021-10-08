# Storing & reading back non-uniform JSON documents via dynamic

This scenario demonstrates how to store and query non-uniform documents via the help of [`dynamic`](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/types/using-type-dynamic).

## Scenario

Let us assume we have a document with non-uniform records, presenting temperature sensor data whereby individual records are identified either via the field _detector_ or _sensor_.

<!-- snippet: sample_sample-scenarios-dynamic-records -->
<a id='snippet-sample_sample-scenarios-dynamic-records'></a>
```cs
// Our documents with non-uniform structure
var records = new dynamic[]
{
    new {sensor = "aisle-1", timestamp = "2020-01-21 11:19:19.283", temperature = 21.2},
    new {sensor = "aisle-2", timestamp = "2020-01-21 11:18:19.220", temperature = 21.6},
    new {sensor = "aisle-1", timestamp = "2020-01-21 11:17:19.190", temperature = 21.6},
    new {detector = "aisle-1", timestamp = "2020-01-21 11:16:19.100", temperature = 20.9},
    new {sensor = "aisle-3", timestamp = "2020-01-21 11:15:19.037", temperature = 21.7,},
    new {detector = "aisle-1", timestamp = "2020-01-21 11:14:19.100", temperature = -1.0}
};
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Scenarios/persist_and_query_via_dynamic.cs#L24-L35' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-scenarios-dynamic-records' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To store and later read back these records, we create a wrapper type with a `dynamic` property to present our record.

<!-- snippet: sample_sample-scenarios-dynamic-type -->
<a id='snippet-sample_sample-scenarios-dynamic-type'></a>
```cs
public class TemperatureData
{
    public int Id { get; set; }
    public dynamic Values { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Scenarios/persist_and_query_via_dynamic.cs#L13-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-scenarios-dynamic-type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

We then read and serialize our records into our newly introduced intermediate type and persist an array of its instances via `BulkInsert`. Lastly, we read back the records, via the non-generic `Query` extension method, passing in predicates that take into account the non-uniform fields of the source documents. After reading back the data for _sensor-1_, we calculate the average of its recorded temperatures:

<!-- snippet: sample_sample-scenarios-dynamic-insertandquery -->
<a id='snippet-sample_sample-scenarios-dynamic-insertandquery'></a>
```cs
var docs = records.Select(x => new TemperatureData {Values = x}).ToArray();

// Persist our records
theStore.BulkInsertDocuments(docs);

using (var session = theStore.OpenSession())
{
    // Read back the data for "aisle-1"
    dynamic[] tempsFromDb = session.Query(typeof(TemperatureData),
        "where data->'Values'->>'detector' = :sensor OR data->'Values'->>'sensor' = :sensor",
        new {sensor = "aisle-1"}).ToArray();

    var temperatures = tempsFromDb.Select(x => (decimal)x.Values.temperature);

    Assert.Equal(15.675m, temperatures.Average());
    Assert.Equal(4, tempsFromDb.Length);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Scenarios/persist_and_query_via_dynamic.cs#L37-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-scenarios-dynamic-insertandquery' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
