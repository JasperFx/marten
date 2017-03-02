# Basic comparisons on a single long property

-> id = 4ef32583-858c-4032-a4db-e1691152c925
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-20T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> String = False
    -> Number = False
    |> TheDocumentsAre-row Name=A, Long=1
    |> TheDocumentsAre-row Name=B, Long=2
    |> TheDocumentsAre-row Name=C, Long=3
    |> TheDocumentsAre-row Name=D, Long=4
    |> TheDocumentsAre-row Name=E, Long=5
    |> TheDocumentsAre-row Name=F, Long=6

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row Results=A
    ``` WhereClause
    x.Long == 1
    ```

    |> ExecutingQuery-row WhereClause=x.Long > 3
    ``` Results
    D, E, F
    ```

    |> ExecutingQuery-row WhereClause=x.Long < 3
    ``` Results
    A, B
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Long <= 3
    ```

    ``` Results
    A, B, C
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Long >= 3
    ```

    ``` Results
    C, D, E, F
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Long != 3
    ```

    ``` Results
    A, B, D, E, F
    ```


~~~
