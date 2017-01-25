# Basic comparisons on a single integer property

-> id = deb452b4-e9ce-49d9-ab54-77868463162c
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-20T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> String = False
    -> Long = False
    |> TheDocumentsAre-row Name=A, Number=1
    |> TheDocumentsAre-row Name=B, Number=2
    |> TheDocumentsAre-row Name=C, Number=3
    |> TheDocumentsAre-row Name=D, Number=4
    |> TheDocumentsAre-row Name=E, Number=5
    |> TheDocumentsAre-row Name=F, Number=6

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row Results=A
    ``` WhereClause
    x.Number == 1
    ```

    |> ExecutingQuery-row WhereClause=x.Number > 3
    ``` Results
    D, E, F
    ```

    |> ExecutingQuery-row WhereClause=x.Number < 3
    ``` Results
    A, B
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Number <= 3
    ```

    ``` Results
    A, B, C
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Number >= 3
    ```

    ``` Results
    C, D, E, F
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Number != 3
    ```

    ``` Results
    A, B, D, E, F
    ```


~~~
