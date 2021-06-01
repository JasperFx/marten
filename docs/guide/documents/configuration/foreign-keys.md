# Foreign Keys

Marten **is** built on top of a relational database, so why not take advantage of those abilities
where they still add value? In this case, Marten allows for a special kind of "Searchable" column
that also adds a foreign key constraint to enforce referential integrity between document types.

One of our sample document types in Marten is the `Issue` class that has
a couple properties that link to the id's of related `User` documents:

<!-- snippet: sample_Issue -->
<!-- endSnippet -->

If I want to enforce referential integrity between the `Issue` document and the `User` documents,
I can use this syntax shown below to configure Marten:

<!-- snippet: sample_configure-foreign-key -->
<!-- endSnippet -->

With the configuration above, Marten will make an `assignee_id` field in the database table and build a
foreign key constraint to the `User` document like so:

```sql
ALTER TABLE public.mt_doc_issue
ADD CONSTRAINT mt_doc_issue_assignee_id_fkey FOREIGN KEY (assignee_id)
REFERENCES public.mt_doc_user (id);

CREATE INDEX mt_doc_issue_idx_assignee_id ON public.mt_doc_issue ("assignee_id");
```

And some other things you probably want to know about how this works internally:

Marten is smart enough to order the "upsert" operations to make the dependent documents be updated last.
In the `Issue` referencing `User` example above, this means that if you create a new `User` and a new
`Issue` in the same session, when you call `IDocumentSession.SaveChanges()/SaveChangesAsync()`, Marten will know
to save the new user first so that the issue will not fail with referential integrity violations.

## Foreign Keys to non-Marten tables

Marten can also create a foreign key to tables that are not managed by Marten. Continuing the our sample
of `Issue`, we can create a foreign key from our `Issue` to our external bug tracking system:

<!-- snippet: sample_configure-external-foreign-key -->
<!-- endSnippet -->

With the configuration above, Marten will generate a foreign key constraint from the `Issue` to a table in the
`bug-tracker` schema called `bugs` on the `id` column.  The constraint would be defined as:

```sql
ALTER TABLE public.mt_doc_issue
ADD CONSTRAINT mt_doc_issue_bug_id_fkey FOREIGN KEY (bug_id)
REFERENCES bug-tracker.bugs (id);
```

## Cascading deletes

Marten can also cascade deletes on the foreign keys that it creates.  The `ForeignKeyDefinition` has a
`CascadeDeletes` property that indicates whether the foreign key should enable cascading deletes.  One way
to enable this is to use a configuration function like:

<!-- snippet: sample_cascade_deletes_with_config_func -->
<!-- endSnippet -->

## Configuring with Attributes

You can optionally configure properties or fields as foreign key relationships with the `[ForeignKey]` attribute:

<!-- snippet: sample_issue-with-fk-attribute -->
<!-- endSnippet -->
