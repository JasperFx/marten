# Build Continuously as Events Come In with a Custom Schema Name

-> id = f3514a2d-7ca8-4e0a-a3b3-6129bf8c647d
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-03-31T20:22:10.3099834Z
-> tags = 

[AsyncDaemon]
|> EventSchemaIs schema=events
|> LeadingEdgeBuffer seconds=1
|> StartTheDaemon
|> PublishAllEventsAsync
|> StopWhenFinished
|> CompareProjects
~~~
