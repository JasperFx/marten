using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class duplicated_field : IntegratedFixture
    {
        [Fact]
        public void can_insert_document_with_duplicated_field_with_DuplicatedFieldEnumStorage_set_to_string()
        {
            StoreOptions(options =>
            {
                options.DuplicatedFieldEnumStorage = EnumStorage.AsString;

                options.Storage.MappingFor(typeof(Target))
                    .DuplicateField(nameof(Target.Color));
            });

            var document = Target.Random();
            document.Color = Colors.Red;

            using (var session = theStore.OpenSession())
            {
                session.Insert(document);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var documentFromDb = query.Load<Target>(document.Id);

                documentFromDb.ShouldNotBeNull();
                documentFromDb.Color.ShouldBe(document.Color);
            }
        }
    }
}