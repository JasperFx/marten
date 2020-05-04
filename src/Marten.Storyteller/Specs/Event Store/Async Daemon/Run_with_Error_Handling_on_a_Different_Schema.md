# Run with Error Handling on a Different Schema

-> id = cc2c6a38-6533-43b1-81c3-a77c80e26ca4
-> lifecycle = Regression
-> max-retries = 3
-> last-updated = 2020-05-04T15:26:00.5082410Z
-> tags = 

[AsyncDaemon]
|> EventSchemaIs schema=events
|> LeadingEdgeBuffer seconds=0
|> RetryThreeTimesOnDivideByZeroException
|> UseTheErroringProjection
|> StartTheDaemon
|> PublishAllEventsAsync
|> StopWhenFinished
|> CompareProjects
~~~
