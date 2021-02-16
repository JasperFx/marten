# Types

PostgreSQL offers a lot more range when it comes to data types, and also allow you to easily create your own. However if you find yourself trying to translate your SQL Server knowledge to Postgres, you may run into some gotcha’s.

The following table is a list of common data types from SQL Server and the Postgres equivalent.

    | SQL Server   | PostgreSQL    | Notes                                                                                                                       |
    |--------------|---------------|-----------------------------------------------------------------------------------------------------------------------------|
    | int          | int / integer |                                                                                                                             |
    | int IDENTITY | serial        | serial is the equivalent of SQL Servers auto generated number and is stored as an integer. Your C# code will still use int. |
    | bit          | boolean       | Postgres has an actual boolean data type.                                                                                   |

## serial

Serial is interesting, because it can actually be used on multiple tables at the same time.

If you define a table like so:

    create table if not exists serial_one (
        id serial,
        name text
    );

    insert into serial_one (name) values ('phill');
    insert into serial_one (name) values ('james');

    select * from serial_one;

You will get a result with two values. And the `id` column will be incremented as you would expect.

You can then query for sequences in Postgres by selecting from the `information_schema.sequences` table.

    select * from information_schema.sequences;

There will be an entry for the sequence created by the table definition above.

![](/images/postgres-sequence.png)

The naming is: table_column_seq

In this instance, the table is `serial_one`, the column is `id`, and the suffix is `seq`

If you look at the table schema you can see the column is created with a default value of `nextval('serial_one_id_seq'::regclass)`

So you can create your own table using the same sequence name, by defining the column as `int` with a default value.

    create table if not exists serial_two (
        id int not null default(nextval('serial_one_id_seq')),
        name text
    );

    insert into serial_two (name) values ('demi');
    insert into serial_two (name) values ('nigel');

    select * from serial_two;

If you want to name the sequence yourself you can create the sequence first like:

    create sequence my_own_named_sequence

Look at the Postgres Sequence docs for more info.

https://www.postgresql.org/docs/current/sql-createsequence.html

## boolean

The boolean type is great to work with, you can use it in many ways. For example, assuming we had a user table with a boolean column `active`

We could use various different queries like so:

    select * from users where active;
    select * from users where not active;

We can also use true/false instead of implicit checks.

    select * from users where active is true;
    select * from users where active is false;

    -- or

    select * from users where active = true;
    select * from users where active = false;;

    -- or

    select * from users where active is not true;
    select * from users where active is not false;
