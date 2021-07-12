# Command Timeouts

By default, Marten just uses the underlying timeout configuration from the [Npgsql connection string](http://www.npgsql.org/doc/connection-string-parameters.html).
You can though, opt to set a different command timeout per session with this syntax:

<!-- snippet: sample_ConfigureCommandTimeout -->
<a id='snippet-sample_configurecommandtimeout'></a>
```cs
public void ConfigureCommandTimeout(IDocumentStore store)
{
    // Sets the command timeout for this session to 60 seconds
    // The default is 30
    using (var session = store.OpenSession(new SessionOptions {Timeout = 60}))
    {

    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/SessionOptionsTests.cs#L14-L24' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configurecommandtimeout' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
