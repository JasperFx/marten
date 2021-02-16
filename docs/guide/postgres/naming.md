# Naming Conventions

When you design your database schema in SQL Server, it's common to name your tables and columns using pascal casing. For example, in SQL Server we may have a table like so:

    | Table Name | Product          |
    |------------|------------------|
    | Columns    | Id               |
    |            | Name             |
    |            | Price            |
    |            | IsDeleted        |
    |            | CategoryId       |
    |            | CreatedByUser    |
    |            | ModifiedByUser   |

PostgreSQL stores all table and columns (that are not in double quotes) in lowercase, so the above would be stored as `product` rather than `Product`, if you run a select with uppercase against Postgres, the query will fail saying the column doesn’t exist. Thus, the Postgres convention for tables and columns, is to name everything lowercase with under scores. The above would become:

    | Table Name | product          |
    |------------|------------------|
    | Columns    | id               |
    |            | name             |
    |            | price            |
    |            | is_deleted       |
    |            | category_id      |
    |            | created_by_user  |
    |            | modified_by_user |

While it is possible to use the convention from SQL Server, if you're looking at the table and column information from the database you will find it is stored in lowercase, this often makes it harder to read later.

For example, if we created a table in Postgres, the same as we would in SQL Server.

```sql
create table if not exists Product (
    Id serial,
    Name text,
    Price money,
    IsDeleted bool,
    CategoryId int,
    CreatedByUser int,
    ModifiedByUser int
);
```

When we query the table definition:

```sql
select *
from INFORMATION_SCHEMA.COLUMNS
where table_name = 'product';
```

You can see from the screen grab that the table and columns are stored lowercase.

![](/images/postgres-table-definition.png)
