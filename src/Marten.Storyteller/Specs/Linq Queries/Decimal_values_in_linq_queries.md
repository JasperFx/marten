# Decimal values in linq queries

-> id = a7e9240c-a679-4eb4-b090-bd6c5881fa58
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
    -> Double = False
    |> TheDocumentsAre-row Name=A, Decimal=1
    |> TheDocumentsAre-row Name=B, Decimal=5
    |> TheDocumentsAre-row Name=C, Decimal=6.5
    |> TheDocumentsAre-row Name=D, Decimal=10
    |> TheDocumentsAre-row Name=E, Decimal=10.1
    |> TheDocumentsAre-row Name=F, Decimal=15

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row Results=D
    ``` WhereClause
    x.Decimal == 10
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Decimal != 10
    ```

    ``` Results
    A, B, C, E, F
    ```

    |> ExecutingQuery-row WhereClause=x.Decimal > 10
    ``` Results
    E, F
    ```

    |> ExecutingQuery-row WhereClause=x.Decimal < 10
    ``` Results
    A, B, C
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Decimal <= 10
    ```

    ``` Results
    A, B, C, D
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Decimal >= 10
    ```

    ``` Results
    D, E, F
    ```


~~~
