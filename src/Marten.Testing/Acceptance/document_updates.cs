using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class document_updates: IntegratedFixture
    {
        [Fact]
        public void can_update_existing_documents()
        {
            var targets = Target.GenerateRandomData(99).ToArray();
            theStore.BulkInsert(targets);

            var theNewNumber = 54321;
            using (var session = theStore.OpenSession())
            {
                targets[0].Double = theNewNumber;
                session.Update(targets[0]);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(targets[0].Id)
                    .Double.ShouldBe(theNewNumber);
            }
        }

        [Fact]
        public void update_sad_path()
        {
            var target = Target.Random();

            using (var session = theStore.OpenSession())
            {
                Exception<NonExistentDocumentException>.ShouldBeThrownBy(() =>
                {
                    session.Update(target);
                    session.SaveChanges();
                });
            }
        }

        [Fact]
        public async Task update_sad_path_async()
        {
            var target = Target.Random();

            using (var session = theStore.OpenSession())
            {
                await Exception<NonExistentDocumentException>.ShouldBeThrownByAsync(async () =>
                {
                    session.Update(target);
                    await session.SaveChangesAsync();
                });
            }
        }
    }
}
