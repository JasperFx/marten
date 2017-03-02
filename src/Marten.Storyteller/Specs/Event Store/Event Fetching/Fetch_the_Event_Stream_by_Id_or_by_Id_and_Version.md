# Fetch the Event Stream by Id or by Id and Version

-> id = b112ba8e-2619-4d27-86f3-b8ffb51a0de9
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2016-04-01T00:00:00.0000000
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

|> AllTheCapturedEventsShouldBe
    [Rows]
    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Quest Destroy the Ring started
    ```

    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Members Frodo, Sam, Merry, Pippin joined at Hobbiton on Day 1
    ```

    |> AllTheCapturedEventsShouldBe-row expected=Arrived at Bree on Day 3
    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Members Strider joined at Bree on Day 4
    ```

    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Arrived at Rivendell on Day 10
    ```

    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Members Gimli, Gandalf, Boromir, Legolas joined at Rivendell on Day 15
    ```

    |> AllTheCapturedEventsShouldBe-row
    ``` expected
    Arrived at Moria on Day 25
    ```


|> FetchEventsByVersion version=3
    [Rows]
    |> null-row
    ``` expected
    Quest Destroy the Ring started
    ```

    |> null-row
    ``` expected
    Members Frodo, Sam, Merry, Pippin joined at Hobbiton on Day 1
    ```

    |> null-row expected=Arrived at Bree on Day 3

|> FetchEventsByVersion version=5, mode=Synchronously
    [Rows]
    |> null-row
    ``` expected
    Quest Destroy the Ring started
    ```

    |> null-row
    ``` expected
    Members Frodo, Sam, Merry, Pippin joined at Hobbiton on Day 1
    ```

    |> null-row expected=Arrived at Bree on Day 3
    |> null-row
    ``` expected
    Members Strider joined at Bree on Day 4
    ```

    |> null-row
    ``` expected
    Arrived at Rivendell on Day 10
    ```


|> FetchEventsByVersion version=5, mode=Asynchronously
    [Rows]
    |> null-row
    ``` expected
    Quest Destroy the Ring started
    ```

    |> null-row
    ``` expected
    Members Frodo, Sam, Merry, Pippin joined at Hobbiton on Day 1
    ```

    |> null-row expected=Arrived at Bree on Day 3
    |> null-row
    ``` expected
    Members Strider joined at Bree on Day 4
    ```

    |> null-row
    ``` expected
    Arrived at Rivendell on Day 10
    ```


~~~
