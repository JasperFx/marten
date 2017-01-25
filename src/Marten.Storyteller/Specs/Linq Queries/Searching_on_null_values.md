# Searching on null values

-> id = 1bd6aa73-27a5-4577-8e97-0e243305ca26
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-28T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> String = True
    -> Number = False
    -> Long = False
    |> TheDocumentsAre-row Name=A, String=NULL
    |> TheDocumentsAre-row Name=B, String=B
    |> TheDocumentsAre-row Name=C, String=NULL
    |> TheDocumentsAre-row Name=D, String=A
    |> TheDocumentsAre-row Name=E, String=E
    |> TheDocumentsAre-row Name=F, String=NULL

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row
    ``` WhereClause
    x.String == null
    ```

    ``` Results
    A, C, F
    ```


~~~
