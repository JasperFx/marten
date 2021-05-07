using System;
using System.IO;
using Baseline;
using Marten.Storage;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Schema.Testing.Bugs
{
    public class Bug_145_table_getting_erroneously_regenerated_Tests: IntegrationContext
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

            theStore.Tenancy.Default.StorageFor<Login>().ShouldNotBeNull();
        }

        [Fact]
        public void does_not_regenerate_the_login_table()
        {
            var existing = theStore.TableSchema(typeof(Login));

            var mapping = theStore.Storage.MappingFor(typeof(Login));
            var configured = new DocumentTable(mapping.As<DocumentMapping>());

            var delta = new TableDelta(configured, existing);
            delta.Difference.ShouldBe(SchemaPatchDifference.None);

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
