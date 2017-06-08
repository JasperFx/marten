using System;
using System.IO;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_145_table_getting_erroneously_regenerated_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        public Bug_145_table_getting_erroneously_regenerated_Tests()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Login>()
                .GinIndexJsonData()
                .Duplicate(x => x.Email)
                .Duplicate(x => x.Identifier);
            });


            theStore.Tenancy.Default.StorageFor(typeof(Login)).ShouldNotBeNull();
        }

        [Fact]
        public void does_not_regenerate_the_login_table()
        {
            var existing = theStore.TableSchema(typeof(Login));

            var mapping = theStore.Tenancy.Default.MappingFor(typeof(Login));
            var configured = new DocumentTable(mapping.As<DocumentMapping>());

            if (!existing.Equals(configured))
            {

                var writer = new StringWriter();
                writer.WriteLine("Expected:");
                configured.Write(theStore.Schema.DdlRules, writer);
                writer.WriteLine();
                writer.WriteLine("But from the database, was:");
                existing.Write(theStore.Schema.DdlRules, writer);

                throw new Exception(writer.ToString());
            }
        }
    }

    public class Login
    {
        public int Id { get; set; }

        public string Email { get; set; }

        public string Password { get; set; }

        public string Salt { get; set; }

        public int MemberId { get; set; }

        public Guid Identifier { get; set; }

        public StatusEnum Status { get; set; }

        public enum StatusEnum
        {
            /// <summary>
            /// Registered but not verified
            /// </summary>
            Registered,

            /// <summary>
            /// Verified their email address
            /// </summary>
            Verified,

            Banned
        }
    }
}