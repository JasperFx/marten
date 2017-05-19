# Loading by Id Array

-> id = 8fb1b539-a120-422c-8872-fa4f8853fd0f
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-05-19T20:17:27.2525251Z
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
    |Blue  |Bill    |User     |
    |Blue  |Hank    |AdminUser|
    |Blue  |Tim     |AdminUser|
    |Green |Chris   |User     |
    |Green |Belle   |User     |
    |Green |Scott   |AdminUser|

|> LoadByIdArray
    [table]
    |tenant|Names            |returnValue    |
    |Blue  |Tom, Bill, Hank  |Tom, Bill, Hank|
    |Blue  |Tom, Bill, Chris |Tom, Bill      |
    |Green |Tim, Chris, Belle|Chris, Belle   |

~~~
