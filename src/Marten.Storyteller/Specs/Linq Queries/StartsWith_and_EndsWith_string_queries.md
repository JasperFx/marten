# StartsWith and EndsWith string queries

-> id = 695a1ad4-1307-4bb5-b3c4-c73075333fd3
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-28T00:00:00.0000000
-> tags = 

[Linq]
|> TheDocumentsAre
    [Rows]
    -> Number = False
    -> Long = False
    -> String = True
    -> Flag = False
    -> Double = False
    -> Decimal = False
    -> Date = False
    -> InnerFlag = False
    |> TheDocumentsAre-row Name=A, String=Barbarian
    |> TheDocumentsAre-row Name=B, String=FooThing
    |> TheDocumentsAre-row Name=C, String=FooBar
    |> TheDocumentsAre-row Name=D, String=LinqFoo
    |> TheDocumentsAre-row Name=E, String=Barbary Pirates
    |> TheDocumentsAre-row Name=F, String=Different
    |> TheDocumentsAre-row Name=G, String=bar and foo

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row
    ``` WhereClause
    x.String.StartsWith("Bar")
    ```

    ``` Results
    A, E
    ```

    |> ExecutingQuery-row WhereClause=x.String.EndsWith("Foo"), Results=D
    |> ExecutingQuery-row
    ``` WhereClause
    x.String.StartsWith("bar", StringComparison.OrdinalIgnoreCase)
    ```

    ``` Results
    A, E, G
    ```


~~~
