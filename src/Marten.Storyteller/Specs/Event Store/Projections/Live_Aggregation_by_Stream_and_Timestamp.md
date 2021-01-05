# Live Aggregation by Stream and Timestamp

-> id = 0b5a8153-e963-461b-b7d7-8fc53bf4143d
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2021-01-05T18:45:09.9469630Z
-> tags = 

[EventStore]
|> ForNewQuestStream name=Destroy the Ring, date=6/1/2015
|> HasAdditionalEvents
    [QuestEvent]
    |> MembersJoinedAt day=1, location=Hobbiton
    ``` names
    Frodo, Sam, Merry, Pippin
    ```

    |> Arrived location=Bree, day=3
    |> MembersJoinedAt names=Strider, day=4, location=Bree
    |> Arrived location=Rivendell, day=10
    |> MembersJoinedAt day=15, location=Rivendell
    ``` names
    Gimli, Gandalf, Boromir, Legolas
    ```


|> OverwriteTimestamps
    [table]
    |version|time   |
    |1      |TODAY-8|
    |2      |TODAY-7|
    |3      |TODAY-7|
    |4      |TODAY-5|
    |5      |TODAY-5|
    |6      |TODAY  |
    |7      |TODAY  |

|> FetchMode mode=Synchronously
|> LiveAggregationToQueryPartyByTimestampShouldBe timestamp=TODAY-3
``` returnValue
Quest party 'Destroy the Ring' is Frodo, Sam, Merry, Pippin, Strider
```

|> FetchMode mode=Asynchronously
|> LiveAggregationToQueryPartyByTimestampShouldBe timestamp=TODAY-3
``` returnValue
Quest party 'Destroy the Ring' is Frodo, Sam, Merry, Pippin, Strider
```

|> FetchMode mode=In a batch
~~~
