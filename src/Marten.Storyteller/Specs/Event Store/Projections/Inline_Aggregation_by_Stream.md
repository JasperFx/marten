# Inline Aggregation by Stream

-> id = 870c1c7c-b2bf-441f-92ad-c8e94b390f7d
-> lifecycle = Acceptance
-> max-retries = 3
-> last-updated = 2017-05-22T21:38:44.1893169Z
-> tags = 

[InlineAggregation]
|> ForNewQuestStream name=Destroy the Ring, date=TODAY-25
|> HasAdditionalEvents
    [QuestEvent]
    |> MembersJoinedAt day=1, location=Hobbiton
    ``` names
    Frodo, Sam
    ```

    |> MembersJoinedAt day=3, location=Shire
    ``` names
    Merry, Pippin
    ```

    |> MembersJoinedAt names=Strider, day=5, location=Bree

|> ForNewQuestStream name=Find the Orb, date=TODAY-10
|> HasAdditionalEvents
    [QuestEvent]
    |> MembersJoinedAt day=1, location=Faldor's Farm
    ``` names
    Garion, Belgarath, Polgara
    ```

    |> MembersJoinedAt day=3, location=Sendaria
    ``` names
    Silk, Barak
    ```

    |> MembersJoinedAt names=Hettar, day=10, location=Algaria

|> ForStream streamName=Destroy the Ring
|> QuestPartyShouldBe Name=Destroy the Ring
``` Members
Frodo, Merry, Pippin, Sam, Strider
```

|> ForStream streamName=Find the Orb
|> QuestPartyShouldBe Name=Find the Orb
``` Members
Barak, Belgarath, Garion, Hettar, Polgara, Silk
```

~~~
