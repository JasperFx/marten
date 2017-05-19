# Non Default Document Filters

-> id = 8d509712-2b48-4a5d-b2be-34e9b42e1233
-> lifecycle = Regression
-> max-retries = 0
-> last-updated = 2017-05-19T13:52:37.8310810Z
-> tags = 

[DocumentFiltering]
|> AdditiveFilters
    [table]
    |baseWhere                  |softDeleted|tenancy  |user                                                                                  |subclass                                                                                                               |
    |x => x.UserName == "Aubrey"|False      |Single   |d.data ->> 'UserName' = :arg0                                                         |(d.data ->> 'UserName' = :arg0 and d.mt_doc_type = 'admin_user')                                                       |
    |x => x.UserName == "Aubrey"|True       |Single   |(d.data ->> 'UserName' = :arg0 and d.mt_deleted = False)                              |(d.data ->> 'UserName' = :arg0 and d.mt_doc_type = 'admin_user' and d.mt_deleted = False)                            |
    |x => x.UserName == "Aubrey"|False      |Conjoined|(d.data ->> 'UserName' = :arg0 and d.tenant_id = :tenantid)|(d.data ->> 'UserName' = :arg0 and d.mt_doc_type = 'admin_user' and d.tenant_id = :tenantid)                         |
    |x => x.UserName == "Aubrey"|True       |Conjoined|(d.data ->> 'UserName' = :arg0 and d.mt_deleted = False and d.tenant_id = :tenantid)  |(d.data ->> 'UserName' = :arg0 and d.mt_doc_type = 'admin_user' and d.mt_deleted = False and d.tenant_id = :tenantid)|
    |x => x.MaybeDeleted()      |False      |Single   |d.mt_deleted is not null                                                              |(d.mt_deleted is not null and d.mt_doc_type = 'admin_user')                                                            |
    |x => x.MaybeDeleted()      |True       |Single   |d.mt_deleted is not null                                                              |(d.mt_deleted is not null and d.mt_doc_type = 'admin_user')                                                            |
    |x => x.MaybeDeleted()      |True       |Conjoined|(d.mt_deleted is not null and d.tenant_id = :tenantid)                                |(d.mt_deleted is not null and d.mt_doc_type = 'admin_user' and d.tenant_id = :tenantid)                                |

~~~
