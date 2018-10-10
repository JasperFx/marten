using System;
using Marten.Schema;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_986_duplicated_fields_with_int_array : IntegratedFixture
    {
        [Fact]
        public void can_insert_new_docs()
        {
            var guyWithIntArray = new GuyWithIntArray { Numbers = new[] { 1, 3, 5, 7 } };

            using (var session = theStore.LightweightSession())
            {
                session.Store(guyWithIntArray);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                session.Load<GuyWithIntArray>(guyWithIntArray.Id).Numbers.ShouldHaveTheSameElementsAs(1, 3, 5, 7);
            }
        }
    }

    public class GuyWithIntArray
    {
        public Guid Id { get; set; }

        [DuplicateField]
        public int[] Numbers { get; set; }
    }
}