# Run with Error Handling on a Different Schema

-> id = cc2c6a38-6533-43b1-81c3-a77c80e26ca4
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-03-31T20:21:06.6249834Z
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
