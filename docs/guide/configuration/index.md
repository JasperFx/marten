
# Integration and Configuration

To add Marten to a .Net project, first go get the Marten library from Nuget:

Using .NET CLI

```shell
dotnet add package Marten
```

Or, using PowerShell

```powershell
PM> Install-Package Marten
```

Or, using [Paket](https://fsprojects.github.io/Paket/):

```shell
paket add nuget Marten
```

The next step is to get access to a PostgreSQL **9.6+** database schema. If you want to let Marten build database schema objects on the fly at development time,
make sure that your user account has rights to execute `CREATE TABLE/FUNCTION` statements.

Marten uses the [Npgsql](http://www.npgsql.org) library to access PostgreSQL from .NET, so you'll likely want to read their [documentation on connection string syntax](http://www.npgsql.org/doc/connection-string-parameters.html).


::: tip
Remember the movie Highlander? In the case of `DocumentStore`, there should be only one.
`DocumentStore` is an expensive object to create that tracks any necessary
development time database changes and also stores all the dynamically created runtime objects that
Marten needs to use at runtime.
:::

To start up Marten in a running application, you need to create a single `IDocumentStore` object. The quickest way is to start with
all the default behavior and a connection string:

<!-- snippet: sample_start_a_store -->
<a id='snippet-sample_start_a_store'></a>
```cs
var store = DocumentStore
    .For("host=localhost;database=marten_testing;password=mypassword;username=someuser");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L34-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_start_a_store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->



* TODO -- explain `StoreOptions` a little bit.
* TODO -- Link to major sections for with and without the HostBuilder

