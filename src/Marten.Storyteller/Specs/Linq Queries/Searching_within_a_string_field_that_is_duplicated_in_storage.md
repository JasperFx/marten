# Searching within a string field that is duplicated in storage

-> id = 17b30540-f57c-4844-bd38-13eb3afdb2e4
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-11-04T00:00:00.0000000
-> tags = 

[Linq]
|> FieldIsDuplicated field=String
|> TheDocumentsAre
    [Rows]
    -> Number = False
    -> Long = False
    |> TheDocumentsAre-row Name=First, String=ABC, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=Second, String=CDE, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=Third, String=BAT, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=Fourth, String=TOM, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row WhereClause=x.String.Contains("B"
    ``` Results
    First, Third
    ```


~~~
