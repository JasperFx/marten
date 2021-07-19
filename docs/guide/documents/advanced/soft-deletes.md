# Soft Deletes

You can opt into using "soft deletes" for certain document types. Using this option means that documents are
never actually deleted out of the database. Rather, a `mt_deleted` field is marked as true and a `mt_deleted_at`
field is updated with the transaction timestamp. If a document type is "soft deleted," Marten will automatically filter out
documents marked as _deleted_ unless you explicitly state otherwise in the Linq `Where` clause.

## Configuring a Document Type as Soft Deleted

You can direct Marten to make a document type soft deleted by either marking the class with an attribute:

<!-- snippet: sample_SoftDeletedAttribute -->
<a id='snippet-sample_softdeletedattribute'></a>
```cs
[SoftDeleted]
public class SoftDeletedDoc
{
    public Guid Id;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/configuring_mapping_deletion_style.cs#L21-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_softdeletedattribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or by using the fluent interface off of `StoreOptions`:

<!-- snippet: sample_soft-delete-configuration-via-fi -->
<a id='snippet-sample_soft-delete-configuration-via-fi'></a>
```cs
DocumentStore.For(_ =>
{
    _.Schema.For<User>().SoftDeleted();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/configuring_mapping_deletion_style.cs#L56-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_soft-delete-configuration-via-fi' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying a "Soft Deleted" Document Type

By default, Marten quietly filters out documents marked as deleted from Linq queries as demonstrated
in this acceptance test from the Marten codebase:

<!-- snippet: sample_query_soft_deleted_docs -->
<a id='snippet-sample_query_soft_deleted_docs'></a>
```cs
[Fact]
public void query_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using (var session = theStore.OpenSession())
    {
        session.Store(user1, user2, user3, user4);
        session.SaveChanges();

        // Deleting 'bar' and 'baz'
        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        session.SaveChanges();

        // no where clause, deleted docs should be filtered out
        session.Query<User>().OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList().ShouldHaveTheSameElementsAs("foo", "jack");

        // with a where clause
        session.Query<User>().Where(x => x.UserName != "jack")
        .ToList().Single().UserName.ShouldBe("foo");
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/soft_deletes.cs#L285-L313' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_soft_deleted_docs' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The SQL generated for the first call to `Query<User>()` above would be:

```sql
select d.data ->> 'UserName' from public.mt_doc_user as d where mt_deleted = False order by d.data ->> 'UserName'
```

## Fetching All Documents, Deleted or Not

You can include deleted documents with Marten's `MaybeDeleted()` method in a Linq `Where` clause
as shown in this acceptance tests:

<!-- snippet: sample_query_maybe_soft_deleted_docs -->
<a id='snippet-sample_query_maybe_soft_deleted_docs'></a>
```cs
[Fact]
public void query_maybe_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using (var session = theStore.OpenSession())
    {
        session.Store(user1, user2, user3, user4);
        session.SaveChanges();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        session.SaveChanges();

        // no where clause, all documents are returned
        session.Query<User>().Where(x => x.MaybeDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList().ShouldHaveTheSameElementsAs("bar", "baz", "foo", "jack");

        // with a where clause, all documents are returned
        session.Query<User>().Where(x => x.UserName != "jack" && x.MaybeDeleted())
            .OrderBy(x => x.UserName)
            .ToList()
            .Select(x => x.UserName)
            .ShouldHaveTheSameElementsAs("bar", "baz", "foo");
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/soft_deletes.cs#L315-L345' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_maybe_soft_deleted_docs' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Fetching Only Deleted Documents

You can also query for only documents that are marked as deleted with Marten's `IsDeleted()` method
as shown below:

<!-- snippet: sample_query_is_soft_deleted_docs -->
<a id='snippet-sample_query_is_soft_deleted_docs'></a>
```cs
[Fact]
public void query_is_soft_deleted_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using (var session = theStore.OpenSession())
    {
        session.Store(user1, user2, user3, user4);
        session.SaveChanges();

        session.DeleteWhere<User>(x => x.UserName.StartsWith("b"));
        session.SaveChanges();

        // no where clause
        session.Query<User>().Where(x => x.IsDeleted()).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList().ShouldHaveTheSameElementsAs("bar", "baz");

        // with a where clause
        session.Query<User>().Where(x => x.UserName != "baz" && x.IsDeleted())
            .OrderBy(x => x.UserName)
            .ToList()
            .Select(x => x.UserName)
            .Single().ShouldBe("bar");
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/soft_deletes.cs#L347-L377' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_is_soft_deleted_docs' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Fetching Documents Deleted before or after a specific time

To search for documents that have been deleted before a specific time use Marten's `DeletedBefore(DateTimeOffset)` method
and the counterpart `DeletedSince(DateTimeOffset)` as show below:

<!-- snippet: sample_query_soft_deleted_since -->
<a id='snippet-sample_query_soft_deleted_since'></a>
```cs
[Fact]
public void query_is_soft_deleted_since_docs()
{
    var user1 = new User { UserName = "foo" };
    var user2 = new User { UserName = "bar" };
    var user3 = new User { UserName = "baz" };
    var user4 = new User { UserName = "jack" };

    using (var session = theStore.OpenSession())
    {
        session.Store(user1, user2, user3, user4);
        session.SaveChanges();

        session.Delete(user3);
        session.SaveChanges();

        var epoch = session.MetadataFor(user3).DeletedAt;
        session.Delete(user4);
        session.SaveChanges();

        session.Query<User>().Where(x => x.DeletedSince(epoch.Value)).Select(x => x.UserName)
            .ToList().ShouldHaveTheSameElementsAs("jack");
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/soft_deletes.cs#L379-L405' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_soft_deleted_since' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

_Neither `DeletedSince` nor `DeletedBefore` are inclusive searches as shown_
