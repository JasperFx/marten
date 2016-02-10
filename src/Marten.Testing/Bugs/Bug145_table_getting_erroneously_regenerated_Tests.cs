using System;
using System.IO;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug145_table_getting_erroneously_regenerated_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        public Bug145_table_getting_erroneously_regenerated_Tests()
        {
            theStore.Schema.Alter(_ => _.For<Login>()
                .GinIndexJsonData()
                .Searchable(x => x.Email)
                .Searchable(x => x.Identifier));

            theStore.Schema.StorageFor(typeof(Login)).ShouldNotBeNull();
        }

        [Fact]
        public void does_not_regenerate_the_login_table()
        {
            var existing = theStore.Schema.TableSchema(typeof(Login));

            var configured = theStore.Schema.MappingFor(typeof(Login)).ToTable(theStore.Schema);

            if (!existing.Equals(configured))
            {

                var writer = new StringWriter();
                writer.WriteLine("Expected:");
                configured.Write(writer);
                writer.WriteLine();
                writer.WriteLine("But from the database, was:");
                existing.Write(writer);

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