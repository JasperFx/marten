using Marten.Schema;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class MartenRegistryExamples
    {
        public MartenRegistryExamples()
        {
            // SAMPLE: using_marten_registry_to_bootstrap_document_store
            var store = DocumentStore.For(_ =>
            {
                _.Connection("your connection string");
                _.Schema.Include<MyMartenRegistry>();
            });
            // ENDSAMPLE

            // SAMPLE: index-last-modified-via-fi
            DocumentStore.For(_ =>
            {
                _.Schema.For<User>().IndexLastModified();
            });
            // ENDSAMPLE
        }
    }

    // SAMPLE: MyMartenRegistry
    public class MyMartenRegistry : MartenRegistry
    {
        public MyMartenRegistry()
        {
            // I'm going to search for user by UserName
            // pretty frequently, so I want that to be
            // a duplicated, searchable field
            For<User>().Duplicate(x => x.UserName);

            // Add a gin index to Company's json data storage
            For<Company>().GinIndexJsonData();
        }
    }

    // ENDSAMPLE

    // SAMPLE: using_attributes_on_document
    [PropertySearching(PropertySearching.ContainmentOperator)]
    public class Employee
    {
        public int Id;

        // You can optionally override the Postgresql
        // type for the duplicated column in the document
        // storage table
        [DuplicateField(PgType = "text")]
        public string Category;
    }

    // ENDSAMPLE

    // SAMPLE: IndexExamples
    public class IndexExamples : MartenRegistry
    {
        public IndexExamples()
        {
            // Add a gin index to the User document type
            For<User>().GinIndexJsonData();

            // Adds a basic btree index to the duplicated
            // field for this property that also overrides
            // the Postgresql database type for the column
            For<User>().Duplicate(x => x.FirstName, pgType: "varchar(50)");

            // Customize the index on the duplicated field
            // for FirstName
            For<User>().Duplicate(x => x.FirstName, configure: idx =>
            {
                idx.IndexName = "idx_special";
                idx.Method = IndexMethod.hash;
            });

            // Customize the index on the duplicated field
            // for UserName to be unique
            For<User>().Duplicate(x => x.UserName, configure: idx =>
            {
                idx.IsUnique = true;
            });
        }
    }

    // ENDSAMPLE
}