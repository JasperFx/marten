# Introduction

Welcome to the Marten documentation! Join our friendly [Discord channel](https://discord.gg/WMxrvegf8H) to learn more with us and the community!

## What is Marten?

**Marten is a .NET library for building applications using
a [document-based approach](https://en.wikipedia.org/wiki/Document-oriented_database)
and [Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html).
** We're committed to removing boilerplate work and letting you focus on delivering business value.

Under the hood, we're using [Postgresql](https://www.postgresql.org/), changing it into:

- a [document database](/documents/),
- an [event store](/events/).

The basis for our work is the unique [Postgresql support for JSON storage](https://www.postgresql.org/docs/current/datatype-json.html).

**Thanks to that and other Postgresql capabilities, Marten brings strong consistency into those approaches.**

Marten is feature-rich and focused on accessibility, and we do that without compromising performance.

Whether you're working on a new greenfield project or a bigger enterprise one, Marten will help you to quickly iterate and evolve your system with a focus on business value.

## Main features

Some of the highlights of the main Marten features:

|                                             Feature                                              |                                                                                                                                                                      Description                                                                                                                                                                       |
|:------------------------------------------------------------------------------------------------:|:------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|
|                                 [Document Storage](/documents/)                                  |                                                                               Marten allows you to use Postgresql as a document database. That makes development much more flexible, as you store your entities as JSON. It helps you to evolve your data model easily.                                                                                |
|                                     [Event store](/events/)                                      |                                                                                          Accordingly, you can also use Postgresql as a full-fledged event store for Event Sourcing. This approach can help you capture all the business facts in your system.                                                                                          |
|               [Strong consistency](/documents/sessions.md#unit-of-work-mechanics)                |                                                                                         Marten uses Postgresql transactions capabilities to allow you to have trust in your storage engine. That applies to both document-based and Event Sourcing approaches.                                                                                         |
|                   [Advanced Linq querying capabilities](/documents/querying/)                    |                                                                You can filter your documents using the LINQ queries. That's the most popular .NET way of doing that. We also support [full-text search](/documents/full-text.md) and [custom SQL queries](/documents/querying/sql.md).                                                                 |
|                            [Events Projections](/events/projections/)                            |        Marten has a unique feature to store both events and read models in the same storage. You can write your projections and get flexibility in interpreting your events. Projections can be applied [in the same transaction](/events/projections/inline.md) as an appended event or [asynchronously](/events/projections/async-daemon.md).        |
|                       [Automatic schema management](/schema/migrations.md)                       |                                                                          We know that schema management in relational databases can be tedious, so that's why we're offering to deal with it for you. Thanks to the simpler storage with JSON format, that gets much easier.                                                                           |
|                       [Flexible indexing strategies](/documents/indexing/)                       |                                                                                           To get better performance, you can define various indexing strategies to fit your usage characteristics. Document-based approach doesn't have to mean schema-less!                                                                                           |
| [ASP.NET integration](/configuration/cli.html) and [Command Line tooling](/configuration/cli.md) |                                                                                                             We provided a set of build-in helpers to get you quickly integrated with Marten in your applications without much of a hassle.                                                                                                             |
|              [Built-in support for Multi-tenancy](/configuration/multitenancy.html)              | Being able to have data isolation for different customers is an essential feature for a storage engine. We provide multiple ways of dealing with multi-tenancy: multiple databases, different schemas, and sharded-table. Those strategies apply to both [document](/documents/multi-tenancy.html) and [event store](/events/multitenancy.html) parts. |

## .NET Version Compatibility

Marten aligns with the [.NET Core Support Lifecycle](https://dotnet.microsoft.com/platform/support/policy/dotnet-core) to determine platform support. Marten currently targets `net6.0`, `net7.0`, `net8.0`.

| Marten Version |   .NET Framework   |   .NET Core 3.1    |       .NET 5       |       .NET 6       |       .NET 7       |       .NET 8       |
|----------------|:------------------:|:------------------:|:------------------:|:------------------:|:------------------:|:------------------:|
| 7              |        :x:         |        :x:         |        :x:         | :white_check_mark: | :white_check_mark: | :white_check_mark: |
| 6              |        :x:         |        :x:         |        :x:         | :white_check_mark: | :white_check_mark: |        :x:         |
| 5              |        :x:         | :white_check_mark: | :white_check_mark: | :white_check_mark: |        :x:         |        :x:         |
| 4              |        :x:         | :white_check_mark: | :white_check_mark: | :white_check_mark: |        :x:         |        :x:         |
| 3              | :white_check_mark: | :white_check_mark: |        :x:         |        :x:         |        :x:         |        :x:         |

## Postgres Version Compatibility

Marten aligns with the [PostgreSQL Support Lifecycle](https://www.postgresql.org/support/versioning/) to determine platform support. Marten currently targets versions 12 and onwards.

## History and origins

Marten was originally built to replace RavenDB inside a very large web application that was suffering stability and performance issues. The project name
_Marten_ came from a quick Google search one day for "what are the natural predators of ravens?" -- which led to us to use the [marten](https://en.wikipedia.org/wiki/Marten) as our project codename and avatar.

![A Marten](/images/marten.jpeg)

The Marten project was publicly announced in late 2015 and quickly gained a solid community of interested developers. An event sourcing feature set was added, which proved popular with our users. Marten first went into a production system in 2016 and has been going strong ever since. The v4 release in 2021 marks a massive overhaul of Marten's internals and introduces new functionality requested by our users to better position Marten for the future.
