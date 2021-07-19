# Noda Time support

Noda Time is an alternative date and time API for .NET. It was written by Jon Skeet to solve many flaws of original .NET api. Since version 4.0 Npgsql supports and suggests it as recommended way of working with Date and Time.

See more in:

- [Npgsql documentation](https://www.npgsql.org/doc/types/nodatime.html)
- [NodaTime documentation](https://nodatime.org/)
- [Jon Skeet blog post about issues with DateTime](https://blog.nodatime.org/2011/08/what-wrong-with-datetime-anyway.html)

## Setup

Marten provides **Marten.NodaTime** plugin. That provides nessesary setup.

Install it through the [Nuget package](https://www.nuget.org/packages/Marten.NodaTime/).

```powershell
PM> Install-Package Marten.NodaTime
```

Then call `UseNodaTime()` method in your `DocumentStore` setup:

<!-- snippet: sample_noda_time_default_setup -->
<a id='snippet-sample_noda_time_default_setup'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    // sets up NodaTime handling
    _.UseNodaTime();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.NodaTime.Testing/Acceptance/noda_time_acceptance.cs#L24-L32' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_noda_time_default_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

By default it also sets up the `JsonNetSerializer` options (see more details in [NodaTime documentation](https://nodatime.org/2.4.x/api/NodaTime.Serialization.JsonNet.Extensions.html)).

If you're using custom Json serializer or you'd like to maintain fully its configuration then you can set disable default configuration by setting `shouldConfigureJsonNetSerializer` parameter to `false`. By changing this setting you need to configure NodaTime Json serialization by yourself.

<!-- snippet: sample_noda_time_setup_without_json_net_serializer_configuration -->
<a id='snippet-sample_noda_time_setup_without_json_net_serializer_configuration'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    _.Serializer<CustomJsonSerializer>();

    // sets up NodaTime handling
    _.UseNodaTime(shouldConfigureJsonNetSerializer: false);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.NodaTime.Testing/Acceptance/noda_time_acceptance.cs#L37-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_noda_time_setup_without_json_net_serializer_configuration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
By using NodaTime plugin - you're opting out of DateTime type handling. Using DateTime in your Document will end up getting NotSupportedException exception.
:::
