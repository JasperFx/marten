using System;
using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1225_duplicated_fields_with_user_defined_type : IntegratedFixture
    {
        [Fact]
        public void can_insert_new_docs()
        {
            var guyWithCustomType = new GuyWithCustomType { CustomType = "Test" };

            using (var session = theStore.LightweightSession())
            {
                session.Store(guyWithCustomType);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Load<GuyWithCustomType>(guyWithCustomType.Id).CustomType.ShouldBe("Test");
            }
        }
    }

    public class GuyWithCustomType
    {
        public Guid Id { get; set; }

        [DuplicateField(PgType = "name")]
        public string CustomType { get; set; }
    }
}
