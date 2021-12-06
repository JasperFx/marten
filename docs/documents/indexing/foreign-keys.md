# Foreign Keys

Marten **is** built on top of a relational database, so why not take advantage of those abilities
where they still add value? In this case, Marten allows for a special kind of "Searchable" column
that also adds a foreign key constraint to enforce referential integrity between document types.

One of our sample document types in Marten is the `Issue` class that has
a couple properties that link to the id's of related `User` documents:

<!-- snippet: sample_Issue -->
<a id='snippet-sample_issue'></a>
```cs
public class Issue
{
    public Issue()
    {
        Id = Guid.NewGuid();
    }

    public string[] Tags { get; set; }

    public Guid Id { get; set; }

    public string Title { get; set; }

    public int Number { get; set; }

    public Guid? AssigneeId { get; set; }

    public Guid? ReporterId { get; set; }

    public Guid? BugId { get; set; }
    public string Status { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Documents/Issue.cs#L5-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_issue' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If I want to enforce referential integrity between the `Issue` document and the `User` documents,
I can use this syntax shown below to configure Marten:

<!-- snippet: sample_configure-foreign-key -->
<a id='snippet-sample_configure-foreign-key'></a>
```cs
var store = DocumentStore
    .For(_ =>
         {
             _.Connection("some database connection");

             // In the following line of code, I'm setting
             // up a foreign key relationship to the User document
             _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
         });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ForeignKeyExamples.cs#L11-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure-foreign-key' title='Start of snippet'>anchor</a></sup>
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
<a id='snippet-sample_configure-external-foreign-key'></a>
```cs
var store = DocumentStore
    .For(_ =>
         {
             _.Connection("some database connection");

             // Here we create a foreign key to table that is not
             // created or managed by marten
             _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugtracker", "bugs", "id");
         });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ForeignKeyExamples.cs#L29-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure-external-foreign-key' title='Start of snippet'>anchor</a></sup>
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
<a id='snippet-sample_cascade_deletes_with_config_func'></a>
```cs
var store = DocumentStore
    .For(_ =>
         {
             _.Connection("some database connection");

             _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.OnDelete = CascadeAction.Cascade);
         });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ForeignKeyExamples.cs#L44-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_cascade_deletes_with_config_func' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Configuring with Attributes

You can optionally configure properties or fields as foreign key relationships with the `[ForeignKey]` attribute:

<!-- snippet: sample_issue-with-fk-attribute -->
<a id='snippet-sample_issue-with-fk-attribute'></a>
```cs
public class Issue
{
    public Issue()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    [ForeignKey(typeof(User))]
    public Guid UserId { get; set; }

    public Guid? OtherUserId { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/configuring_foreign_key_fields_Tests.cs#L66-L82' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_issue-with-fk-attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
