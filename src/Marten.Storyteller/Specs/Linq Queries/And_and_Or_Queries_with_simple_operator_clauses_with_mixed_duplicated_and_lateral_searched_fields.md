# And and Or Queries with simple operator clauses with mixed duplicated and lateral searched fields

-> id = 85f13bfa-6e98-4919-a557-03fd16324ce1
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-11-04T00:00:00.0000000
-> tags = 

[Linq]
|> FieldIsDuplicated field=Number
|> TheDocumentsAre
    [Rows]
    -> Long = False
    |> TheDocumentsAre-row Name=First, Number=1, String=A, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=Second, Number=2, String=A, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=Third, Number=1, String=B, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=Fourth, Number=1, String=A, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False
    |> TheDocumentsAre-row Name=Fifth, Number=2, String=C, Flag=false, Double=1, Decimal=1, Date=TODAY, InnerFlag=False

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row
    ``` WhereClause
    x.String == "A" && x.Number == 1
    ```

    ``` Results
    First, Fourth
    ```

    |> ExecutingQuery-row
    ``` WhereClause
    x.String == "A" || x.Number == 1
    ```

    ``` Results
    First, Second, Third, Fourth
    ```


~~~
