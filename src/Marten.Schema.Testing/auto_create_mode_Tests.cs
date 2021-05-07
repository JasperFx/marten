using System;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Schema.Testing
{
    public class auto_create_mode_Tests : IntegrationContext
    {
        [Fact(Skip = "sample usage code")]
        public void using_auto_create_field()
        {
            #region sample_AutoCreateSchemaObjects
            var store = DocumentStore.For(_ =>
            {
                // Marten will create any new objects that are missing,
                // attempt to update tables if it can, but drop and replace
                // tables that it cannot patch.
                _.AutoCreateSchemaObjects = AutoCreate.All;


                // Marten will create any new objects that are missing or
                // attempt to update tables if it can. Will *never* drop
                // any existing objects, so no data loss
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;


                // Marten will create missing objects on demand, but
                // will not change any existing schema objects
                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;

                // Marten will not create or update any schema objects
                // and throws an exception in the case of a schema object
                // not reflecting the Marten configuration
                _.AutoCreateSchemaObjects = AutoCreate.None;
            });
            #endregion sample_AutoCreateSchemaObjects
        }

	    [Fact]
	    public void DefaultAutoCreateShouldBeCreateOrUpdate()
	    {
		    var settings = new StoreOptions();

			Assert.Equal(AutoCreate.CreateOrUpdate, settings.AutoCreateSchemaObjects);
	    }

		[Fact]
	    public void DefaultAutoCreateShouldBeCreateOrUpdateWhenProvidingNoConfig()
		{
			var store = DocumentStore.For("");

			Assert.Equal(AutoCreate.CreateOrUpdate, store.Options.AutoCreateSchemaObjects);
	    }

        [Fact]
        public void cannot_add_fields_if_mode_is_create_only()
        {
            var user1 = new User { FirstName = "Jeremy" };
            var user2 = new User { FirstName = "Max" };
            var user3 = new User { FirstName = "Declan" };

            theStore.BulkInsert(new User[] { user1, user2, user3 });

            using (var store2 = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOnly;
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<User>().Duplicate(x => x.FirstName);
            }))
            {
                var ex = Should.Throw<SchemaMigrationException>(() =>
                {
                    store2.Tenancy.Default.EnsureStorageExists(typeof(User));
                });

            }
        }


    }


}
