# Build Continuously as Events Come In

-> id = 76f147bc-4c17-4202-bf8d-2b96e25e1f44
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-03-31T19:35:55.6829853Z
-> tags = 

[AsyncDaemon]
|> LeadingEdgeBuffer seconds=1
|> StartTheDaemon
|> PublishAllEventsAsync
|> StopWhenFinished
|> CompareProjects
~~~
