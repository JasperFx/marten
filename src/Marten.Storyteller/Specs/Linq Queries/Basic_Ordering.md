# Basic Ordering

-> id = c32aed5d-e6cc-4746-b8e3-c0e480c6140d
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2015-10-27T00:00:00.0000000
-> tags = 

[Ordering]
|> TheDocumentsAre
    [Rows]
    |> TheDocumentsAre-row Name=A, Number=1, Long=1, String=Max
    |> TheDocumentsAre-row Name=B, Number=2, Long=3, String=Jeremy
    |> TheDocumentsAre-row Name=C, Number=4, Long=2, String=Lindsey
    |> TheDocumentsAre-row Name=D, Number=1, Long=1, String=Abby
    |> TheDocumentsAre-row Name=E, Number=5, Long=6, String=Arthur

|> ExecutingQuery
    [table]
    |> ExecutingQuery-row
    ``` Query
    OrderBy(x => x.String)
    ```

    ``` Results
    D, E, B, C, A
    ```

    |> ExecutingQuery-row
    ``` Query
    OrderByDescending(x => x.String)
    ```

    ``` Results
    A, C, B, E, D
    ```

    |> ExecutingQuery-row
    ``` Query
    OrderBy(x => x.Number).ThenBy(x => x.String)
    ```

    ``` Results
    D, A, B, C, E
    ```

    |> ExecutingQuery-row
    ``` Query
    OrderBy(x => x.Number).ThenByDescending(x => x.String)
    ```

    ``` Results
    A, D, B, C, E
    ```

    |> ExecutingQuery-row
    ``` Query
    OrderByDescending(x => x.Number).ThenBy(x => x.String)
    ```

    ``` Results
    E, C, B, D, A
    ```


~~~
