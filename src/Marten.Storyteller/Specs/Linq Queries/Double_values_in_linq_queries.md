# Double values in linq queries

-> id = 5f577cd7-f64e-4c1f-a2b4-848462ee97ac
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-28T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> InnerFlag = False
    -> Flag = False
    -> String = False
    -> Long = False
    -> Number = False
    |> TheDocumentsAre-row Name=A, Double=1
    |> TheDocumentsAre-row Name=B, Double=5
    |> TheDocumentsAre-row Name=C, Double=6.5
    |> TheDocumentsAre-row Name=D, Double=10
    |> TheDocumentsAre-row Name=E, Double=10.1
    |> TheDocumentsAre-row Name=F, Double=15

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row Results=D
    ``` WhereClause
    x.Double == 10
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Double != 10
    ```

    ``` Results
    A, B, C, E, F
    ```

    |> ExecutingQuery-row WhereClause=x.Double > 10
    ``` Results
    E, F
    ```

    |> ExecutingQuery-row WhereClause=x.Double < 10
    ``` Results
    A, B, C
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Double <= 10
    ```

    ``` Results
    A, B, C, D
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Double >= 10
    ```

    ``` Results
    D, E, F
    ```


~~~
