<!--Title:Escaping-->
<!--Url:escaping-->

In SQL Server, escaping is done by wrapping with square brackets.

    select [Id] from [MyTable];

In Postgres this is done using double quotes.

    select "id" from "my_table";
