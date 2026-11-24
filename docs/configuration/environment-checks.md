# Environment Checks

Marten takes part in the [environment checks](https://jeremydmiller.com/2019/10/01/environment-checks-and-better-command-line-abilities-for-your-net-core-application/)
built into the JasperFx command line tooling that comes with Marten. When you use that tooling as your application's
command line parser (which you are if you're using Marten's [command line tooling](/configuration/cli)),
`dotnet run -- check-env` asserts that each of every Marten store's databases matches the configured schema, and runs
any registered `IHealthCheck`. There is nothing to add for Marten: each store registers itself as a JasperFx system part,
and its databases are the resources that `check-env` checks.

## Asserting the database on startup

Separately from `check-env`, `AddMarten().AssertDatabaseMatchesConfigurationOnStartup()` asserts that every database of
the store matches the configured state when the application starts. That is not an environment check: it runs in the
hosted service Marten registers to activate the store, so a mismatch throws out of the host's `StartAsync()` and the
application does not start:

<!-- snippet: sample_use_environment_check_in_hosted_service -->
<a id='snippet-sample_use_environment_check_in_hosted_service'></a>
```cs
public static async Task use_environment_check()
{
    using var host = await Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            // The assertion runs in the hosted service Marten registers
            // to activate the store, so a database that does not match
            // the configuration throws out of StartAsync() and the host
            // never starts
            services.AddMarten("connection string")
                .AssertDatabaseMatchesConfigurationOnStartup();
        })
        .StartAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/EnvironmentChecks.cs#L10-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_use_environment_check_in_hosted_service' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
