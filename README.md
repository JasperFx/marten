# Marten _Postgresql as a Document Database and Event Store for .Net Applications_

[![Join the chat at https://gitter.im/JasperFx/Marten](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/JasperFx/Marten?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)


Hey, we're just getting started, but there'll be stuff here soon. Check the issue list as quasi-roadmap and feel free to jump into the Gitter room linked above.
See this blog post http://jeremydmiller.com/2015/10/21/postgresql-as-a-document-db-for-net-development/ for more information

## Working with the Code

Like I said, it's way, way early and this should get smoother later. For now, you'll need to have access to a Postgresql **9.4** server and a database. After cloning the code, put a file named `connection.txt` at `src/Marten.Testing` that just needs to contain the connection string to the Postgresql database you want to use as a testbed. See the [Npgsql documentation](http://www.npgsql.org/doc/connection-string-parameters.html) for more information about postgresql connection strings.

You will also need to enable the PLV8 extension inside of Postgresql for running Javascript stored procedures for the nascent projection support. See
[this link](http://www.postgresonline.com/journal/archives/341-PLV8-binaries-for-PostgreSQL-9.4-windows-both-32-bit-and-64-bit.html) for pre-built binaries for PLV8 running on Windows. Just drop the folder structure from that download into your main Postgresql installation folder (`c:\program files\postgresql\9.4` on my box). Once the binaries are copied in, run the command `CREATE EXTENSION PLV8;` in your Postgresql database. 
If you have any trouble with PLV8, please feel free to ask for help in the Gitter room.


Once you have the codebase and the connection.txt file, either:

* Run the rake script
* From a command line at the root of the codebase, run `paket install` to fetch all the nuget dependencies

From there, open Visual Studio.Net or whatever editor you prefer and go to town.

## Tooling

We're using [Fixie](https://github.com/fixie/fixie) and [Shouldly](https://github.com/shouldly/shouldly) for unit testing and [paket](https://fsprojects.github.io/Paket/) for improved Nuget workflow. We're temporarily using rake for build automation.

## Mocha Specs

To run the mocha tests on the little bit of custom Javascript for Marten, you will also need some version of Node.js that at least supports arrow function
syntax (I'm using Node.js 4.*). Use `rake mocha` or `npm install` once, then `npm run test`. There is also `npm run tdd` to run the mocha specifications
in a watched mode with growl turned on. 

## Storyteller Specs

We're also using [Storyteller](http://storyteller.github.io) for some of the very data intensive automated tests. To open the Storyteller editor, use the command `rake open_st` from the command line or `rake storyeller` to run the Storyteller specs.

