<!--title: Noda Time support-->

Noda Time is an alternative date and time API for .NET. It was written by Jon Skeet to solve many flaws of orginal .NET api. Since version 4.0 Npgsql supports and suggests it as recommended way of working with Date and Time.

See more in:
- [Npgsql documentation](https://www.npgsql.org/doc/types/nodatime.html)
- [NodaTime documentation](https://nodatime.org/)
- [Jon Skeet blog post about issues with DateTime](https://blog.nodatime.org/2011/08/what-wrong-with-datetime-anyway.html)

## Setup 

Marten provides **Marten.NodaTime** plugin. That provides nessesary setup. 

Install it through the [Nuget package](https://www.nuget.org/packages/Marten.NodaTime/).

```
PM> Install-Package Marten.NodaTime
```

Then call `UseNodaTime()` method in your `DocumentStore` setup:

<[sample:noda_time_default_setup]>

By default it also sets up the `JsonNetSerializer` options (see more details in [NodaTime documentation](https://nodatime.org/2.4.x/api/NodaTime.Serialization.JsonNet.Extensions.html)).

If you're using custom Json serializer or you'd like to maintain fully its configuration then you can set disable default configuration by setting `shouldConfigureJsonNetSerializer` parameter to `false`. By changing this setting you need to configure NodaTime Json serialization by yourself.

<[sample:noda_time_setup_without_json_net_serializer_configuration]>
