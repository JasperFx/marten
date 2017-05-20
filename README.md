# Marten _Postgresql as a Document Database and Event Store for .Net Applications_

[![Join the chat at https://gitter.im/JasperFx/Marten](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/JasperFx/Marten?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)
[![Windows Build Status](https://img.shields.io/teamcity/http/build.fubu-project.org/s/marten_master.svg?label=TeamCity&style=flat)](http://build.fubu-project.org/project.html?projectId=Marten&tab=projectOverview&guest=1)
[![Nuget Package](https://img.shields.io/nuget/v/Marten.svg?style=flat)](https://www.nuget.org/packages/Marten/)

Hey, we're just getting started, but there'll be stuff here soon. Check the issue list as quasi-roadmap and feel free to jump into the Gitter room linked above.
See this blog post http://jeremydmiller.com/2015/10/21/postgresql-as-a-document-db-for-net-development/ for more information

## Working with the Code

Like I said, it's way, way early and this should get smoother later. For now, you'll need to have access to a Postgresql **9.5** server and a database. After cloning the code, set the environment variable `marten-testing-database` to the connection string for the Postgresql database you want to use as a testbed. See the [Npgsql documentation](http://www.npgsql.org/doc/connection-string-parameters.html) for more information about postgresql connection strings.

You will also need to enable the PLV8 extension inside of Postgresql for running Javascript stored procedures for the nascent projection support. See
[this link](http://www.postgresonline.com/journal/archives/360-PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit.html) for pre-built binaries for PLV8 running on Windows. Just drop the folder structure from that download into your main Postgresql installation folder (`c:\program files\postgresql\9.5` on my box). Once the binaries are copied in, run the command `CREATE EXTENSION PLV8;` in your Postgresql database. 
If you have any trouble with PLV8, please feel free to ask for help in the Gitter room.


Once you have the codebase and the connection.txt file, either:

* Run the rake script
* From a command line at the root of the codebase, run `paket restore` to fetch all the nuget dependencies

From there, open Visual Studio.Net or whatever editor you prefer and go to town.

## Tooling

We're using [xUnit](http://xunit.github.io/) and [Shouldly](https://github.com/shouldly/shouldly) for unit testing and [paket](https://fsprojects.github.io/Paket/) for improved Nuget workflow. We're temporarily using rake for build automation, but it's not mandatory for development.

## Mocha Specs

To run the mocha tests on the little bit of custom Javascript for Marten, you will also need some version of Node.js that at least supports arrow function
syntax (I'm using Node.js 4.*). Use `rake mocha` or `npm install` once, then `npm run test`. There is also `npm run tdd` to run the mocha specifications
in a watched mode with growl turned on. 

## Storyteller Specs

We're also using [Storyteller](http://storyteller.github.io) for some of the very data intensive automated tests. To open the Storyteller editor, use the command `rake open_st` from the command line or `rake storyeller` to run the Storyteller specs. If you don't want to use rake, you can launch the
Storyteller editor *after compiling the solution* by the command `packages\storyteller\tools\st.exe open src/Marten.Testing`.

## Documentation

The documentation website for Marten is authored with [Storyteller's documentation generation feature](http://storyteller.github.io/documentation/docs/). The actual content is the markdown files in the `/documentation` directory directly under the project root. To quickly run the documentation website locally with auto-refresh (it's not perfect since it does rely on .Net's FileSystemWatcher), either use the rake task `rake docs` or there is a new batch script named `run-docs.cmd`. 

If you wish to insert code samples to a documentation page from the tests,
you'll need to wrap the code you wish to insert with
`// SAMPLE: name-of-sample` and `// ENDSAMPLE`.
Then to insert that code to the documentation, add `<[sample:name-of-sample]>`.

The content is kept in the main Marten GitHub repository, but the published documentation is done by running the `publish-docs.cmd` command and pushing the generated static HTML to the gh-pages branch of Marten.  

