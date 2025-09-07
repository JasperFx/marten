using Marten.Schema;
using Marten.Testing.Documents;
using Weasel.Postgresql.Tables;

namespace Marten.Testing.Examples;

public class MartenRegistryExamples
{
    public MartenRegistryExamples()
    {
        #region sample_using_marten_registry_to_bootstrap_document_store
        var store = DocumentStore.For(_ =>
        {
            _.Connection("your connection string");
        });
        #endregion

        #region sample_index-last-modified-via-fi
        DocumentStore.For(_ =>
        {
            _.Schema.For<User>().IndexLastModified();
        });
        #endregion

        #region sample_index-created-timestamp-via-fi
        DocumentStore.For(_ =>
        {
            _.Schema.For<User>().IndexCreatedAt();
        });
        #endregion

        #region sample_index-tenantId-via-fi
        DocumentStore.For(_ =>
        {
            _.Schema.For<User>().MultiTenanted();
            _.Schema.For<User>().IndexTenantId();
        });
        #endregion
    }
}

#region sample_using_attributes_on_document
[PropertySearching(PropertySearching.ContainmentOperator)]
public class Employee
{
    public int Id;

    // You can optionally override the Postgresql
    // type for the duplicated column in the document
    // storage table
    [DuplicateField(PgType = "text")]
    public string Category;

    // Defining a duplicate column with not null constraint
    [DuplicateField(PgType = "text", NotNull = true)]
    public string Department;
}

#endregion


public static class IndexExamples
{
    public static void Configure()
    {
        #region sample_IndexExamples
        var store = DocumentStore.For(options =>
        {
            // Add a gin index to the User document type
            options.Schema.For<User>().GinIndexJsonData();

            // Adds a basic btree index to the duplicated
            // field for this property that also overrides
            // the Postgresql database type for the column
            options.Schema.For<User>().Duplicate(x => x.FirstName, pgType: "varchar(50)");

            // Defining a duplicate column with not null constraint
            options.Schema.For<User>().Duplicate(x => x.Department, pgType: "varchar(50)", notNull: true);

            // Customize the index on the duplicated field
            // for FirstName
            options.Schema.For<User>().Duplicate(x => x.FirstName, configure: idx =>
            {
                idx.Name = "idx_special";
                idx.Method = IndexMethod.hash;
            });

            // Customize the index on the duplicated field
            // for UserName to be unique
            options.Schema.For<User>().Duplicate(x => x.UserName, configure: idx =>
            {
                idx.IsUnique = true;
            });

            // Customize the index on the duplicated field
            // for LastName to be in descending order
            options.Schema.For<User>().Duplicate(x => x.LastName, configure: idx =>
            {
                idx.SortOrder = SortOrder.Desc;
            });
        });
        #endregion
    }
}
