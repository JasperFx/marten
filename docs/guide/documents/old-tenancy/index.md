# Tenancy

Marten supports multi-tenancy to provide data isolation between tenants, aka groups of users. In effect, this allows scoping storage operations, such as persisting and loading data, so that no tenant can access data of others. Marten provides multi-tenancy at the logical level, by associating data records with a tenant identifier. In addition, multi-tenancy through separate databases or schemas is planned.

By default, Marten operates in single-tenancy mode (`TenancyStyle.Single`) with multi-tenancy disabled.
