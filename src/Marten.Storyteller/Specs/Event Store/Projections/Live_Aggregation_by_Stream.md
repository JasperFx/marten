# Live Aggregation by Stream

-> id = 3c10209f-347b-4534-a2e2-35505a5796d6
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2016-04-22T00:00:00.0000000
-> tags = 

[EventStore]
|> ForNewQuestStream name=TheOneRing, date=TODAY
|> HasAdditionalEvents
    [QuestEvent]
    |> MembersJoinedAt day=1, location=The Shire
    ``` names
    Frodo, Sam
    ```

    |> MembersJoinedAt day=2, location=Merry's House
    ``` names
    Merry, Pippin
    ```

    |> MembersJoinedAt names=Strider, day=5, location=Bree
    |> MembersJoinedAt day=10, location=Rivendell
    ``` names
    Gandalf, Legolas, Gimli, Boromir
    ```

    |> MembersDepartedAt day=15, location=The Lake
    ``` names
    Frodo, Sam
    ```


|> FetchMode mode=Synchronously
|> LiveAggregationToQueryPartyShouldBe
``` returnValue
Quest party 'TheOneRing' is Merry, Pippin, Strider, Gandalf, Legolas, Gimli, Boromir
```

|> FetchMode mode=Asynchronously
|> LiveAggregationToQueryPartyShouldBe
``` returnValue
Quest party 'TheOneRing' is Merry, Pippin, Strider, Gandalf, Legolas, Gimli, Boromir
```

|> FetchMode mode=In a batch
|> LiveAggregationToQueryPartyShouldBe
``` returnValue
Quest party 'TheOneRing' is Merry, Pippin, Strider, Gandalf, Legolas, Gimli, Boromir
```

~~~
