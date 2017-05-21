# Querying via Compiled Queries

-> id = 85ce633c-ad67-4725-9e30-4d7de52cab04
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-05-21T15:56:43.5999982Z
-> tags = 

[MultiTenancyQuerying]
|> IfTheStoreIs
    [ConfigureDocumentStore]
    |> UsesConjoinedMultiTenancy

|> TheUsersAre
    [table]
    |tenant|UserName|UserType|
    |Blue  |Tom     |User    |
    |Blue  |Alice   |User    |
    |Blue  |Alex    |User    |
    |Blue  |Todd    |User    |
    |Green |Jill    |User    |
    |Green |Hank    |User    |
    |Green |Albert  |User    |
    |Green |Amos    |User    |

|> Querying
    [table]
    |tenant|Query                                          |returnValue |
    |Blue  |User names starting with 'A' via compiled query|Alice, Alex |
    |Green |User names starting with 'A' via compiled query|Albert, Amos|

~~~
