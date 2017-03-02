# Searching within a string

-> id = dba9a196-2b97-4226-bb19-8e2b41d64c6f
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-20T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> Number = False
    -> Long = False
    |> TheDocumentsAre-row Name=First, String=ABC
    |> TheDocumentsAre-row Name=Second, String=CDE
    |> TheDocumentsAre-row Name=Third, String=BAT
    |> TheDocumentsAre-row Name=Fourth, String=TOM

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row WhereClause=x.String.Contains("B"
    ``` Results
    First, Third
    ```


~~~
