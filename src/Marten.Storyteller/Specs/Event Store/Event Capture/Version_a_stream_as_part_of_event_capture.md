# Version a stream as part of event capture

-> id = 50fbeb18-1477-46c2-8478-32e6ae5dc3af
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2016-03-04T00:00:00.0000000
-> tags = 

[EventStore]
|> ForNewQuestStream name=Find the Orb, date=TODAY

There's only a single event for "QuestStarted", so the version should just be 1

|> TheQuestVersionShouldBe name=Find the Orb, version=1
|> HasAdditionalEvents
    [QuestEvent]
    |> Arrived location=Sendaria, day=5
    |> Arrived location=Algeria, day=15


After capturing the two events above,

|> TheQuestVersionShouldBe name=Find the Orb, version=3
|> AllTheCapturedEventsShouldBe
    [Rows]
    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Quest Find the Orb started
    ```

    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Arrived at Sendaria on Day 5
    ```

    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Arrived at Algeria on Day 15
    ```


~~~
