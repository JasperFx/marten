# Basic Inserting and Querying

-> id = a1f8225c-917b-418d-8640-d46c60c627f3
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-05-19T18:42:08.7834178Z
-> tags = 

[MultiTenancyQuerying]
|> IfTheStoreIs
    [ConfigureDocumentStore]
    |> UsesConjoinedMultiTenancy

|> TheUsersAre
    [table]
    |tenant|UserName|UserType|
    |Blue  |Hank    |User    |
    |Blue  |Adam    |User    |
    |Blue  |Tom     |User    |
    |Green |Bill    |User    |
    |Green |Alex    |User    |
    |Red   |Jill    |User    |
    |Red   |Alice   |User    |

|> Querying
    [table]
    |tenant|Query                           |returnValue    |
    |Blue  |All Users                       |Hank, Adam, Tom|
    |Green |All Users                       |Bill, Alex     |
    |Red   |All Users                       |Jill, Alice    |
    |Blue  |All User Names starting with 'A'|Adam           |
    |Green |All User Names starting with 'A'|Alex           |
    |Red   |All User Names starting with 'A'|Alice          |

~~~
