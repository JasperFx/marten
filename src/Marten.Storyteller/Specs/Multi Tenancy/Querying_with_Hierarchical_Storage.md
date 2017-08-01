# Querying with Hierarchical Storage

-> id = eed7f79a-1dbb-4c92-bbe3-870b2052f0a9
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-05-19T18:42:08.7584178Z
-> tags = 

[MultiTenancyQuerying]
|> IfTheStoreIs
    [ConfigureDocumentStore]
    |> UsesConjoinedMultiTenancy
    |> UserIsHierarchical

|> TheUsersAre
    [table]
    |tenant|UserName|UserType |
    |Blue  |Tom     |User     |
    |Blue  |Alice   |User     |
    |Blue  |Alex    |AdminUser|
    |Blue  |Bill    |SuperUser|
    |Green |Jake    |User     |
    |Green |Aiden   |SuperUser|
    |Green |Albert  |AdminUser|

|> Querying
    [table]
    |tenant|Query                             |returnValue           |
    |Blue  |All Users                         |Tom, Alice, Alex, Bill|
    |Blue  |Admin Users                       |Alex                  |
    |Blue  |All User Names starting with 'A'  |Alice, Alex           |
    |Blue  |Admin User Names starting with 'A'|Alex                  |
    |Green |Admin Users                       |Albert                |

~~~
