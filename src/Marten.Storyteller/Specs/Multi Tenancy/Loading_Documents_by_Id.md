# Loading Documents by Id

-> id = 1a213060-4314-4318-9c26-4b2831469b62
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-05-19T19:45:18.2515340Z
-> tags = 

[MultiTenancyQuerying]
|> IfTheStoreIs
    [ConfigureDocumentStore]
    |> UsesConjoinedMultiTenancy
    |> UserIsHierarchical

|> TheUsersAre
    [table]
    |tenant|UserName|UserType |
    |Green |Bill    |User     |
    |Green |Tom     |AdminUser|
    |Blue  |Jake    |User     |
    |Blue  |Rachel  |AdminUser|

|> CanLoadAdminById
    [table]
    |tenant|UserName|returnValue|
    |Green |Bill    |False      |
    |Blue  |Bill    |False      |
    |Green |Tom     |True       |
    |Blue  |Tom     |False      |
    |Blue  |Jake    |False      |
    |Blue  |Rachel  |True       |

|> CanLoadAdminById
    [table]
    |tenant|UserName|returnValue|
    |Green |Bill    |False      |
    |Green |Tom     |True       |
    |Green |Jake    |False      |
    |Green |Rachel  |False      |
    |Blue  |Rachel  |True       |

~~~
