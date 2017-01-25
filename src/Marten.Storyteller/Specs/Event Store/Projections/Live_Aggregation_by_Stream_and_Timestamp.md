# Live Aggregation by Stream and Timestamp

-> id = 0b5a8153-e963-461b-b7d7-8fc53bf4143d
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2016-04-22T00:00:00.0000000
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

    |> Arrived location=Moria, day=25

|> OverwriteTimestamps
    [table]
    |> OverwriteTimestamps-row version=1, time=TODAY-8
    |> OverwriteTimestamps-row version=2, time=TODAY-7
    |> OverwriteTimestamps-row version=3, time=TODAY-7
    |> OverwriteTimestamps-row version=4, time=TODAY-5
    |> OverwriteTimestamps-row version=5, time=TODAY-5
    |> OverwriteTimestamps-row version=6, time=TODAY
    |> OverwriteTimestamps-row version=7, time=TODAY

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
|> LiveAggregationToQueryPartyByTimestampShouldBe timestamp=TODAY-3
``` returnValue
Quest party 'Destroy the Ring' is Frodo, Sam, Merry, Pippin, Strider
```

~~~
