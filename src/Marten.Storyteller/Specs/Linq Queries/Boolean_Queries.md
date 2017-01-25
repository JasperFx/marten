# Boolean Queries

-> id = a551281b-f8cc-498e-a61e-71fc80002957
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-28T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> String = False
    -> Long = False
    -> Number = False
    |> TheDocumentsAre-row Name=A, Flag=false, InnerFlag=False
    |> TheDocumentsAre-row Name=B, Flag=True, InnerFlag=False
    |> TheDocumentsAre-row Name=C, Flag=false, InnerFlag=True
    |> TheDocumentsAre-row Name=D, Flag=True, InnerFlag=True
    |> TheDocumentsAre-row Name=E, Flag=false, InnerFlag=False
    |> TheDocumentsAre-row Name=F, Flag=false, InnerFlag=True

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row WhereClause=x.Flag
    ``` Results
    B, D
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Flag == True
    ```

    ``` Results
    B, D
    ```

    |> ExecutingQuery-row WhereClause=!Flag
    ``` Results
    A, C, E, F
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Flag == False
    ```

    ``` Results
    A, C, E, F
    ```

    |> ExecutingQuery-row WhereClause=Inner.Flag
    ``` Results
    C, D, F
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Inner.Flag == True
    ```

    ``` Results
    C, D, F
    ```

    |> ExecutingQuery-row WhereClause=!Inner.Flag
    ``` Results
    A, B, E
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.Inner.Flag == False
    ```

    ``` Results
    A, B, E
    ```


~~~
