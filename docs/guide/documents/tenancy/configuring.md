# Configuring Tenancy

The three levels of tenancy that Marten supports are expressed in the enum `TenancyStyle` with effective values of:

* `Single`, no multi-tenancy
* `Conjoined`, multi-tenancy through tenant id
* `Separate`, multi-tenancy through separate databases or schemas

Tenancy can be configured at the store level, applying to all documents or, at the most fine-grained level, on individual documents.

## Tenancy Through Policies

Tenancy can be configured through Document Policies, accesible via `StoreOptions.Policies`. The following sample demonstrates setting the default tenancy to `TenancyStyle.Conjoined` for all documents.

<!-- snippet: sample_tenancy-configure-through-policy -->
<!-- endSnippet -->

## Tenancy At Document Level & Policy Overrides

Tenancy can be configured at a document level through document mappings. This also enables overriding store-level configurations applied through Document Policies. The following sample demonstrates setting, through `StoreOptions` the tenancy for `Target` to `TenancyStyle.Conjoined`, making it deviate from the configured default policy of `TenancyStyle.Single`.

<!-- snippet: sample_tenancy-configure-override -->
<!-- endSnippet -->
