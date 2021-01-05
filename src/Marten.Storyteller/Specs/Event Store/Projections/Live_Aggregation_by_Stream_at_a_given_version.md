# Live Aggregation by Stream at a given version

-> id = e71003c6-9676-4a2b-b91b-980e35cf8124
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2021-01-05T18:45:26.0936850Z
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



The first event is a 'QuestStarted` event, so we're starting at #2

|> FetchMode mode=Synchronously
|> LiveAggregationToQueryPartyVersionShouldBe version=2
``` returnValue
Quest party 'TheOneRing' is Frodo, Sam
```

|> LiveAggregationToQueryPartyVersionShouldBe version=3
``` returnValue
Quest party 'TheOneRing' is Frodo, Sam, Merry, Pippin
```

|> LiveAggregationToQueryPartyVersionShouldBe version=4
``` returnValue
Quest party 'TheOneRing' is Frodo, Sam, Merry, Pippin, Strider
```

|> FetchMode mode=Asynchronously
|> LiveAggregationToQueryPartyVersionShouldBe version=2
``` returnValue
Quest party 'TheOneRing' is Frodo, Sam
```

|> LiveAggregationToQueryPartyVersionShouldBe version=3
``` returnValue
Quest party 'TheOneRing' is Frodo, Sam, Merry, Pippin
```

|> LiveAggregationToQueryPartyVersionShouldBe version=4
``` returnValue
Quest party 'TheOneRing' is Frodo, Sam, Merry, Pippin, Strider
```

~~~
