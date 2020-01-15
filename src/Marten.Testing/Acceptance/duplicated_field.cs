using System;
using Marten.Schema;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class duplicated_field: IntegratedFixture
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

        [Fact]
        public void can_insert_document_with_duplicated_field_with_not_null_constraint()
        {
            StoreOptions(options =>
            {
                options.DuplicatedFieldEnumStorage = EnumStorage.AsString;

                options.Storage.MappingFor(typeof(NonNullableDuplicateFieldTestDoc))
                    .DuplicateField(nameof(NonNullableDuplicateFieldTestDoc.NonNullableDuplicateField), notNull: true);
            });

            var document = new NonNullableDuplicateFieldTestDoc
            {
                Id = Guid.NewGuid(),
                NonNullableDuplicateField = DateTime.Now,
                NonNullableDuplicateFieldViaAttribute = DateTime.Now
            };

            using (var session = theStore.OpenSession())
            {
                session.Insert(document);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var documentFromDb = query.Load<NonNullableDuplicateFieldTestDoc>(document.Id);

                documentFromDb.ShouldNotBeNull();
                documentFromDb.NonNullableDuplicateField.ShouldBe(document.NonNullableDuplicateField);
                documentFromDb.NonNullableDuplicateFieldViaAttribute.ShouldBe(document.NonNullableDuplicateFieldViaAttribute);
            }
        }

        [Fact]
        public void can_insert_document_with_duplicated_field_with_null_constraint()
        {
            StoreOptions(options =>
            {
                options.DuplicatedFieldEnumStorage = EnumStorage.AsString;

                // Note: Even though notNull is false by default, setting it to false for the unit test
                options.Storage.MappingFor(typeof(NullableDuplicateFieldTestDoc))
                    .DuplicateField(nameof(NullableDuplicateFieldTestDoc.NullableDuplicateField), notNull: false);
            });

            var document = new NullableDuplicateFieldTestDoc
            {
                Id = Guid.NewGuid()
            };

            using (var session = theStore.OpenSession())
            {
                session.Insert(document);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var documentFromDb = query.Load<NullableDuplicateFieldTestDoc>(document.Id);

                documentFromDb.ShouldNotBeNull();
                documentFromDb.NullableDuplicateField.ShouldBeNull();
                documentFromDb.NullableDuplicateFieldViaAttribute.ShouldBeNull();
            }
        }
    }

    public class NullableDuplicateFieldTestDoc
    {
        public Guid Id { get; set; }
        [DuplicateField] // Note: NotNull is false by default hence not set
        public DateTime? NullableDuplicateFieldViaAttribute { get; set; }
        public DateTime? NullableDuplicateField { get; set; }
    }

    public class NonNullableDuplicateFieldTestDoc
    {
        public Guid Id { get; set; }
        [DuplicateField(NotNull = true)]
        public DateTime NonNullableDuplicateFieldViaAttribute { get; set; }
        public DateTime NonNullableDuplicateField { get; set; }
    }
}
