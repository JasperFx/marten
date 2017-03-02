# Basic comparisons on a single string property

-> id = d5ecff19-88f2-4785-aafd-9db0a6b8762c
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-20T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> String = True
    -> Number = False
    -> Long = False
    |> TheDocumentsAre-row Name=A, String=A
    |> TheDocumentsAre-row Name=B, String=B
    |> TheDocumentsAre-row Name=C, String=C
    |> TheDocumentsAre-row Name=D, String=A
    |> TheDocumentsAre-row Name=E, String=E
    |> TheDocumentsAre-row Name=F, String=F

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row
    ``` WhereClause
    x.String == "A"
    ```

    ``` Results
    A, D
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.String != "A"
    ```

    ``` Results
    B, C, E, F
    ```


~~~
