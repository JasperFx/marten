# Serializing with Jil

Marten has also been tested using the [Jil library](https://github.com/kevin-montrose/Jil) for JSON serialization. While Jil is not as
flexible as Newtonsoft.Json and might be missing support for some scenarios you may encounter, it is very clearly faster than Newtonsoft.Json.

To use Jil inside of Marten, add a class to your system like this one that implements the `ISerializer` interface:

<!-- snippet: sample_JilSerializer -->
<a id='snippet-sample_jilserializer'></a>
```cs
public class JilSerializer : ISerializer
{
    private readonly Options _options
        = new(dateFormat: DateTimeFormat.ISO8601, includeInherited:true);

    public ValueCasting ValueCasting { get; } = ValueCasting.Strict;

    public string ToJson(object document)
    {
        return JSON.Serialize(document, _options);
    }

    public T FromJson<T>(Stream stream)
    {
        return JSON.Deserialize<T>(stream.GetStreamReader(), _options);
    }

    public T FromJson<T>(DbDataReader reader, int index)
    {
        var stream = reader.GetStream(index);
        return FromJson<T>(stream);
    }

    public ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default)
    {
        return new(FromJson<T>(stream));
    }

    public ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default)
    {
        return new (FromJson<T>(reader, index));
    }

    public object FromJson(Type type, Stream stream)
    {
        return JSON.Deserialize(stream.GetStreamReader(), type, _options);
    }

    public object FromJson(Type type, DbDataReader reader, int index)
    {
        var stream = reader.GetStream(index);
        return FromJson(type, stream);
    }

    public ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
    {
        return new (FromJson(type, stream));
    }

    public ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index, CancellationToken cancellationToken = default)
    {
        return new (FromJson(type, reader, index));
    }

    public string ToCleanJson(object document)
    {
        return ToJson(document);
    }

    public EnumStorage EnumStorage => EnumStorage.AsString;
    public Casing Casing => Casing.Default;
    public string ToJsonWithTypes(object document)
    {
        throw new NotSupportedException();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/performance_tuning.cs#L14-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_jilserializer' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Next, replace the default `ISerializer` when you bootstrap your `DocumentStore` as in this example below:

<!-- snippet: sample_replacing_serializer_with_jil -->
<a id='snippet-sample_replacing_serializer_with_jil'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("the connection string");

    // Replace the ISerializer w/ the TestsSerializer
    _.Serializer<TestsSerializer>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/performance_tuning.cs#L92-L100' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_replacing_serializer_with_jil' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

See [Optimizing for Performance in Marten](http://jeremydmiller.com/2015/11/09/optimizing-for-performance-in-marten/) 
and [Optimizing Marten Part 2](http://jeremydmiller.com/2015/11/30/optimizing-marten-part-2/) for some performance comparisons 
of using Jil versus Newtonsoft.Json for serialization within Marten operations.
