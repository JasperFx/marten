# Environment Checks

Marten has a couple options for adding [environment checks](https://jeremydmiller.com/2019/10/01/environment-checks-and-better-command-line-abilities-for-your-net-core-application/) to your application that can assert on whether the Marten database(s)
are in the correct state. The first way is to use [Oakton](https://jasperfx.github.io/oakton) as your command line parser for your application (which you are if you're using Marten's command line tooling) and take advantage
of its built in [environment check](https://jasperfx.github.io/oakton/documentation/hostbuilder/environment/) functionality.

To add an environment check to assert that the actual Marten database matches the configured state, just use the `AddMarten().AddEnvironmentChecks()` extension method that is contained in the Marten.CommandLine library.

Another option is this usage:

<!-- snippet: sample_use_environment_check_in_hosted_service -->
<a id='snippet-sample_use_environment_check_in_hosted_service'></a>
```cs
public static async Task use_environment_check()
{
    using var host = await Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            // Do this, or your environment check assertion failures below
            // is just swallowed and logged on startup
            services.Configure<HostOptions>(options =>
            {
                options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
            });

            services.AddMarten("connection string")
                .AssertDatabaseMatchesConfigurationOnStartup();
        })
        .StartAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/EnvironmentChecks.cs#L10-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_use_environment_check_in_hosted_service' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->