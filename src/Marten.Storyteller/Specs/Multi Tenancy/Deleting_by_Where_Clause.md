# Deleting by Where Clause

-> id = 34c55af0-8068-40b3-886c-b733811dbb02
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-05-19T18:42:08.7734178Z
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
    |Blue  |Amos    |AdminUser|
    |Blue  |Todd    |User     |
    |Green |Austin  |User     |
    |Green |Albert  |AdminUser|

|> DeleteByFilter tenantId=Blue
|> Querying
    [table]
    |tenant|Query    |returnValue   |
    |Blue  |All Users|Tom, Todd     |
    |Green |All Users|Austin, Albert|

|> DeleteAdminByFilter tenantId=Green
|> Querying
    [table]
    |tenant|Query    |returnValue|
    |Green |All Users|Austin     |
    |Blue  |All Users|Tom, Todd  |

~~~
