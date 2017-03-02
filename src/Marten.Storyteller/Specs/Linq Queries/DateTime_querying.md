# DateTime querying

-> id = 78342f05-736a-45db-b850-a5bc8f33976d
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-28T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> Number = False
    -> Long = False
    -> String = False
    -> Flag = False
    -> Double = False
    -> Decimal = False
    -> InnerFlag = False
    |> TheDocumentsAre-row Name=A, Date=TODAY
    |> TheDocumentsAre-row Name=B, Date=TODAY+1
    |> TheDocumentsAre-row Name=C, Date=TODAY+2
    |> TheDocumentsAre-row Name=D, Date=TODAY-1
    |> TheDocumentsAre-row Name=E, Date=TODAY-2
    |> TheDocumentsAre-row Name=F, Date=TODAY

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row
    ``` WhereClause
    x.Date == Today
    ```

    ``` Results
    A, F
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Date != Today
    ```

    ``` Results
    B, C, D, E
    ```

    |> ExecutingQuery-row WhereClause=x.Date > Today
    ``` Results
    B, C
    ```

    |> ExecutingQuery-row WhereClause=x.Date < Today
    ``` Results
    D, E
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Date >= Today
    ```

    ``` Results
    A, B, C, F
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Date <= Today
    ```

    ``` Results
    A, D, E, F
    ```


~~~
