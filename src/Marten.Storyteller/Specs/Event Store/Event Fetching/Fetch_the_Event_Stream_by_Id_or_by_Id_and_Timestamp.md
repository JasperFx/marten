# Fetch the Event Stream by Id or by Id and Timestamp

-> id = f98fd82a-766b-4e1d-b5b6-48d2039b06b3
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

|> OverwriteTimestamps
    [table]
    |> OverwriteTimestamps-row version=1, time=TODAY-8
    |> OverwriteTimestamps-row version=2, time=TODAY-7
    |> OverwriteTimestamps-row version=3, time=TODAY-7
    |> OverwriteTimestamps-row version=4, time=TODAY-5
    |> OverwriteTimestamps-row version=5, time=TODAY-5
    |> OverwriteTimestamps-row version=6, time=TODAY
    |> OverwriteTimestamps-row version=7, time=TODAY

|> FetchEventsByTimestamp time=TODAY-3
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


|> FetchEventsByTimestamp time=TODAY-6
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

|> FetchEventsByTimestamp time=TODAY-3, mode=Asynchronously
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


|> FetchEventsByTimestamp time=TODAY-6, mode=Asynchronously
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

~~~
