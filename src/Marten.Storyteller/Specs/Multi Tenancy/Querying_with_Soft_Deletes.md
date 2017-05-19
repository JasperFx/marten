# Querying with Soft Deletes

-> id = c26e6142-15e6-49be-8409-e1ff08e2c095
-> lifecycle = Acceptance
-> max-retries = 0
-> last-updated = 2017-05-19T16:52:57.9123385Z
-> tags = 

[MultiTenancyQuerying]
|> IfTheStoreIs
    [ConfigureDocumentStore]
    |> UsesConjoinedMultiTenancy
    |> UsersAreSoftDeleted
    |> UserIsHierarchical

|> TheUsersAre
    [table]
    |tenant|UserName|UserType |
    |Blue  |Tom     |User     |
    |Blue  |Todd    |User     |
    |Blue  |Alex    |User     |
    |Blue  |Alice   |AdminUser|
    |Green |Trevor  |User     |
    |Green |Albert  |User     |
    |Green |Al      |AdminUser|

|> Delete name=Tom, tenantId=Blue
|> Delete name=Alex, tenantId=Blue
|> Querying
    [table]
    |> Querying-row tenant=Blue, Query=All Users
    ``` returnValue
    Todd, Alice
    ```


~~~
