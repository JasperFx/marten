# Introduction

Click this [link](https://sec.ch9.ms/ch9/2d29/a281311a-76bb-4573-a2a0-2dd7affc2d29/S315dotNETconf_high.mp4) to watch an introductory video on Marten.

## What is Marten?

Marten is a .NET library that allows developers to use the Postgresql database as both a
[document database](https://en.wikipedia.org/wiki/Document-oriented_database) and a fully-featured [event store](https://martinfowler.com/eaaDev/EventSourcing.html) -- with the document features serving as the out-of-the-box
mechanism for projected "read side" views of your events. There is absolutely nothing else to install or run, outside of the Nuget package and Postgresql itself. Marten was made possible by the unique [JSONB](https://www.postgresql.org/docs/current/datatype-json.html) support first introduced in Postgresql 9.4.

Marten was originally built to replace RavenDB inside a very large web application that was suffering stability and performance issues.
The project name *Marten* came from a quick Google search one day for "what are the natural predators of ravens?" -- which led to us to
use the [marten](https://en.wikipedia.org/wiki/Marten) as our project codename and avatar.

![A Marten](/images/marten.jpeg)

The Marten project was publicly announced in late 2015 and quickly gained a solid community of interested developers. An event sourcing feature set was
added, which proved popular with our users. Marten first went into a production system in 2016 and has been going strong ever since. The v4
release in 2021 marks a massive overhaul of Marten's internals, and introduces new functionality requested by our users to better position Marten for the future.

## .NET Version Compatibility

Marten aligns with the [.NET Core Support Lifecycle](https://dotnet.microsoft.com/platform/support/policy/dotnet-core) to determine platform compatibility. Marten currently targets `net6.0`,  `net7.0`.

| Marten Version |   .NET Framework   |   .NET Core 3.1    |       .NET 5       |       .NET 6       |       .NET 7       |
| -------------- | :----------------: | :----------------: | :----------------: | :----------------: | :----------------: |
| 6              |        :x:         |        :x:         |        :x:         | :white_check_mark: | :white_check_mark: |
| 5              |        :x:         | :white_check_mark: | :white_check_mark: | :white_check_mark: |        :x:         |
| 4              |        :x:         | :white_check_mark: | :white_check_mark: | :white_check_mark: |        :x:         |
| 3              | :white_check_mark: | :white_check_mark: |        :x:         |        :x:         |        :x:         |

### Nullable Reference Types

For enhanced developer ergonomics, Marten is annotated with [NRTs](https://docs.microsoft.com/en-us/dotnet/csharp/nullable-references). For new .NET projects, this is automatically enabled. If updating from previous versions of .NET, this can be opted-into via `<Nullable>enable</Nullable>` within your `.csproj`.

