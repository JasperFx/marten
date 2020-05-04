# Run with Error Handling

-> id = cd77f984-19b6-4df1-ab7d-c065121e98b3
-> lifecycle = Regression
-> max-retries = 3
-> last-updated = 2020-05-04T15:25:33.3830500Z
-> tags = 

[AsyncDaemon]
|> LeadingEdgeBuffer seconds=0
|> RetryThreeTimesOnDivideByZeroException
|> UseTheErroringProjection
|> StartTheDaemon
|> PublishAllEventsAsync
|> StopWhenFinished
|> CompareProjects
~~~
