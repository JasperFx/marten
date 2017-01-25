# And and Or Queries with simple operator clauses

-> id = 531df1e8-45a9-4d47-99eb-3700dc09fdda
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-20T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> Long = False
    |> TheDocumentsAre-row Name=First, Number=1, String=A
    |> TheDocumentsAre-row Name=Second, Number=2, String=A
    |> TheDocumentsAre-row Name=Third, Number=1, String=B
    |> TheDocumentsAre-row Name=Fourth, Number=1, String=A
    |> TheDocumentsAre-row Name=Fifth, Number=2, String=C

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row
    ``` WhereClause
    x.String == "A" && x.Number == 1
    ```

    ``` Results
    First, Fourth
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.String == "A" || x.Number == 1
    ```

    ``` Results
    First, Second, Third, Fourth
    ```


~~~
