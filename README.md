# Marten _Postgresql as a Document Database and Event Store for .Net Applications_

[![Join the chat at https://gitter.im/JasperFx/Marten](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/JasperFx/Marten?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)


Hey, we're just getting started, but there'll be stuff here soon. Check the issue list as quasi-roadmap and feel free to jump into the Gitter room linked above.
See this blog post http://jeremydmiller.com/2015/10/21/postgresql-as-a-document-db-for-net-development/ for more information

## Working with the Code

Like I said, it's way, way early and this should get smoother later. For now, you'll need to have access to a Postgresql 9.5 server and a database. After cloning the code, put a file named `connection.txt` at `src/Marten.Testing` that just needs to contain the connection string to the Postgresql database you want to use as a testbed. See the [Npgsql documentation](http://www.npgsql.org/doc/connection-string-parameters.html) for more information about postgresql connection strings.

Once you have the codebase and the connection.txt file, either:

* Run the rake script
* From a command line at the root of the codebase, run `paket install` to fetch all the nuget dependencies

From there, open Visual Studio.Net or whatever editor you prefer and go to town.

## Tooling

We're using [Fixie](https://github.com/fixie/fixie) and [Shouldly](https://github.com/shouldly/shouldly) for unit testing and [paket](https://fsprojects.github.io/Paket/) for improved Nuget workflow. We're temporarily using rake for build automation.

## Storyteller Specs

We're also using [Storyteller](http://storyteller.github.io) for some of the very data intensive automated tests. To open the Storyteller editor, use the command `rake open_st` from the command line or `rake storyeller` to run the Storyteller specs.

