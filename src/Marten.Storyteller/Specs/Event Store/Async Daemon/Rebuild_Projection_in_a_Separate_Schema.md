# Rebuild Projection in a Separate Schema

-> id = 865489fa-684d-4855-8532-84a3385c9350
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-03-31T20:31:05.6239831Z
-> tags = 

[AsyncDaemon]
|> EventSchemaIs schema=events
|> LeadingEdgeBuffer seconds=0
|> PublishAllEvents
|> RebuildProjection
|> CompareProjects
~~~
