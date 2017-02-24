# Take and Skip

-> id = c50ac2ff-3582-4638-a469-af39c9b4514c
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-02-22T15:34:04.4382461Z
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
    OrderBy(x => x.String).Take(2)
    ```

    ``` Results
    D, E
    ```

    |> ExecutingQuery-row
    ``` Query
    OrderBy(x => x.String).Skip(2)
    ```

    ``` Results
    B, C, A
    ```

    |> ExecutingQuery-row
    ``` Query
    OrderBy(x => x.String).Take(2).Skip(2)
    ```

    ``` Results
    B, C
    ```


~~~


