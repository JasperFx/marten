using Marten.Schema;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class MartenRegistryExamples
    {
        public void building_document_store()
        {
// SAMPLE: using_marten_registry_to_bootstrap_document_store
var store = DocumentStore.For(_ =>
{
    _.Connection("your connection string");
    _.Schema.Include<MyMartenRegistry>();
});
// ENDSAMPLE
        }
    }

    // SAMPLE: MyMartenRegistry
    public class MyMartenRegistry : MartenRegistry
    {
        public MyMartenRegistry()
        {
            // Opt into the Postgres 9.5 upsert style
            UpsertType = PostgresUpsertType.Standard;
            
            // I'm going to search for user by UserName
            // pretty frequently, so I want that to be 
            // a duplicated, searchable field
            For<User>().Searchable(x => x.UserName);


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

        [Searchable]
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
            // field for this property
            For<User>().Searchable(x => x.FirstName);

            // Customize the index on the duplicated field
            // for FirstName 
            For<User>().Searchable(x => x.FirstName, idx =>
            {
                idx.IndexName = "idx_special";
                idx.Method = IndexMethod.hash;
            });

        }
    }
    // ENDSAMPLE

    // SAMPLE: setting_upsert_style
    public class SettingUpsertStyle : MartenRegistry
    {
        public SettingUpsertStyle()
        {
            // To use the traditional upsert style for 9.4 and below:
            UpsertType = PostgresUpsertType.Legacy;

            // To opt into the new 9.5 capabilities
            UpsertType = PostgresUpsertType.Standard;
        }
    }
    // ENDSAMPLE
}