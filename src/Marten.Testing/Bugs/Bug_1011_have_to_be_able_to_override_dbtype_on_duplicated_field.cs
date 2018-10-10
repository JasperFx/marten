using System;
using System.Linq;
using Marten.Schema;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1011_have_to_be_able_to_override_dbtype_on_duplicated_field : IntegratedFixture
    {
        [Fact]
        public void override_the_db_type_with_fluent_interface()
        {
            StoreOptions(_ =>
                {
                    _.Schema.For<DocWithDateTimeField>().Duplicate(x => x.Date, pgType: "timestamp without time zone",
                        dbType: NpgsqlDbType.Timestamp);
                });


            var field = theStore.Storage.MappingFor(typeof(DocWithDateTimeField))
                .DuplicatedFields.Single();
            
            field.DbType.ShouldBe(NpgsqlDbType.Timestamp);
            field.PgType.ShouldBe("timestamp without time zone");
        }
        
        [Fact]
        public void override_the_db_type_with_attribute()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<DocWithDateTimeField>().Duplicate(x => x.Date, pgType: "timestamp without time zone",
                    dbType: NpgsqlDbType.Timestamp);
            });


            var field = theStore.Storage.MappingFor(typeof(DocWithDateTimeField))
                .DuplicatedFields.Single();
            
            field.DbType.ShouldBe(NpgsqlDbType.Timestamp);
            field.PgType.ShouldBe("timestamp without time zone");
        }


        public class DocWithDateTimeField
        {
            public Guid Id { get; set; }
            public DateTime Date { get; set; }
        }
        
        public class DocWithDateTimeField2
        {
            public Guid Id { get; set; }
            
            [DuplicateField(DbType = NpgsqlDbType.Timestamp, PgType = "timestamp without time zone")]
            public DateTime Date { get; set; }
        }
    }
}