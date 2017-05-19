# Deleting by Id

-> id = 85580f2e-6b08-4bad-bfe1-bc0168a0fce8
-> lifecycle = Acceptance
-> max-retries = 0
-> last-updated = 2017-05-19T17:01:52.0939063Z
-> tags = 

[MultiTenancyQuerying]
|> IfTheStoreIs
    [ConfigureDocumentStore]
    |> UsesConjoinedMultiTenancy

|> TheUsersAre
    [table]
    |tenant|UserName|UserType|
    |Blue  |Tom     |User    |
    |Blue  |Todd    |User    |
    |Green |Bill    |User    |
    |Green |Sara    |User    |


The initial state

|> Querying
    [table]
    |tenant|Query    |returnValue|
    |Blue  |All Users|Tom, Todd  |
    |Green |All Users|Bill, Sara |


'Tom' is owned by Blue, so nothing should happen here

|> Delete name=Tom, tenantId=Green
|> Querying
    [table]
    |> Querying-row tenant=Blue, Query=All Users
    ``` returnValue
    Tom, Todd
    ```



'Tom' is owned by Blue, so this should result in the doc being deleted

|> Delete name=Tom, tenantId=Blue
|> Querying
    [table]
    |> Querying-row tenant=Blue, Query=All Users, returnValue=Todd

~~~
