# Environment Checks

Marten has a couple options for adding [environment checks](https://jeremydmiller.com/2019/10/01/environment-checks-and-better-command-line-abilities-for-your-net-core-application/) to your application that can assert on whether the Marten database(s)
are in the correct state. The first way is to use [Oakton](https://jasperfx.github.io/oakton) as your command line parser for your application (which you are if you're using Marten's command line tooling) and take advantage
of its built in [environment check](https://jasperfx.github.io/oakton/documentation/hostbuilder/environment/) functionality. 

To add an environment check to assert that the actual Marten database matches the configured state, just use the `AddMarten().AddEnvironmentChecks()` extension method that 
is contained in the Marten.CommandLine library.
