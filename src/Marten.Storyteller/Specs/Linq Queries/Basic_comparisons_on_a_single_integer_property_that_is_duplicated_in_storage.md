# Basic comparisons on a single integer property that is duplicated in storage

-> id = 23549038-cd8c-4ae9-af4e-db2f68bad92f
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-11-04T00:00:00.0000000
-> tags = 

[Linq]
|> FieldIsDuplicated field=Number
|> TheDocumentsAre
    [Rows]
    -> String = False
    -> Long = False
    |> TheDocumentsAre-row Name=A, Number=1, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=B, Number=2, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=C, Number=3, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=D, Number=4, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=E, Number=5, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=F, Number=6, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False

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
