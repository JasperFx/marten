# Rebuild Projection with Sequential Gaps in the Event Store

-> id = 894d0c8f-b15d-4b8f-a8d9-58c876417682
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-03-31T20:39:29.1119808Z
-> tags = 

[AsyncDaemon]
|> LeadingEdgeBuffer seconds=0
|> PublishAllEvents
|> CreateSequentialGap original=2, seq=20000
|> RebuildProjection
|> CompareProjects
~~~
