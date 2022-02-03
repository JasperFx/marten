using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Indexes
{
    public class duplicated_field: OneOffConfigurationsContext
    {
        [Fact]
        public void can_insert_document_with_duplicated_field_with_DuplicatedFieldEnumStorage_set_to_string()
        {
            StoreOptions(options =>
            {
                options.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;

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

                SpecificationExtensions.ShouldNotBeNull(documentFromDb);
                documentFromDb.Color.ShouldBe(document.Color);
            }
        }

        [Fact]
        public void can_insert_document_with_duplicated_field_with_not_null_constraint()
        {
            StoreOptions(options =>
            {
                options.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;

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

                SpecificationExtensions.ShouldNotBeNull(documentFromDb);
                documentFromDb.NonNullableDuplicateField.ShouldBe(document.NonNullableDuplicateField);
                documentFromDb.NonNullableDuplicateFieldViaAttribute.ShouldBe(document.NonNullableDuplicateFieldViaAttribute);
            }
        }

        [Fact]
        public void can_insert_document_with_duplicated_field_with_null_constraint()
        {
            StoreOptions(options =>
            {
                options.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;

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

                SpecificationExtensions.ShouldNotBeNull(documentFromDb);
                SpecificationExtensions.ShouldBeNull(documentFromDb.NullableDuplicateField);
                SpecificationExtensions.ShouldBeNull(documentFromDb.NullableDuplicateFieldViaAttribute);
            }
        }


                [Fact]
        public void use_the_default_pg_type_for_the_member_type_if_not_overridden()
        {
            var mapping = DocumentMapping.For<Organization>();
            var duplicate = mapping.DuplicatedFields.Single(x => x.MemberName == "Time2");

            duplicate.PgType.ShouldBe("timestamp without time zone");
        }

        [Fact]
        public void creates_btree_index_for_the_member()
        {
            var mapping = DocumentMapping.For<Organization>();
            var indexDefinition = mapping.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == "Name".ToTableAlias());

            indexDefinition.Method.ShouldBe(IndexMethod.btree);
        }

        [Fact]
        public void can_override_index_type_and_name_on_the_attribute()
        {
            var mapping = DocumentMapping.For<Organization>();
            var indexDefinition = (DocumentIndex)mapping.Indexes.Single(x => x.Name == "idx_foo");

            indexDefinition.Method.ShouldBe(IndexMethod.hash);
        }

        [Fact]
        public void can_override_index_sort_order_on_the_attribute()
        {
            var mapping = DocumentMapping.For<Organization>();
            var indexDefinition = mapping.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == "YetAnotherName".ToTableAlias());

            indexDefinition.SortOrder.ShouldBe(SortOrder.Desc);
        }

        [Fact]
        public void can_override_field_type_selection_on_the_attribute()
        {
            var mapping = DocumentMapping.For<Organization>();
            var duplicate = mapping.DuplicatedFields.Single(x => x.MemberName == "Time");

            duplicate.PgType.ShouldBe("timestamp");
        }

        [Fact]
        public void can_override_with_MartenRegistry()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Organization>().Duplicate(x => x.Time2, pgType: "timestamp");
            });

            theStore.Storage.MappingFor(typeof(Organization)).As<DocumentMapping>().DuplicatedFields.Single(x => x.MemberName == "Time2")
                .PgType.ShouldBe("timestamp");
        }

        [Fact]
        public void duplicate_and_search_off_of_deep_accessor_by_number()
        {
            var targets = Target.GenerateRandomData(10).ToArray();
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Duplicate(x => x.Inner.Number);
            });

            targets.Each(x => theSession.Store(x));
            theSession.SaveChanges();

            var thirdTarget = targets.ElementAt(2);

            var results = theSession.Query<Target>().Where(x => x.Inner.Number == thirdTarget.Inner.Number).ToArray();
            results
                .Any(x => x.Id == thirdTarget.Id).ShouldBeTrue();
        }

        [Fact]
        public void duplicate_and_search_off_of_deep_accessor_by_enum()
        {
            var targets = Target.GenerateRandomData(10).ToArray();
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Duplicate(x => x.Inner.Color);
            });

            targets.Each(x => theSession.Store(x));
            theSession.SaveChanges();

            var thirdTarget = targets.ElementAt(2);

            var results = theSession.Query<Target>().Where(x => x.Inner.Color == thirdTarget.Inner.Color).ToArray();
            results
                .Any(x => x.Id == thirdTarget.Id).ShouldBeTrue();
        }

        [Fact]
        public void duplicate_and_search_off_of_deep_accessor_by_date()
        {
            var targets = Target.GenerateRandomData(10).ToArray();
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Duplicate(x => x.Inner.Date);
            });

            targets.Each(x => theSession.Store(x));
            theSession.SaveChanges();

            var thirdTarget = targets.ElementAt(2);

            var queryable = theSession.Query<Target>().Where(x => x.Inner.Date == thirdTarget.Inner.Date);
            var results = queryable.ToArray();
            results
                .Any(x => x.Id == thirdTarget.Id).ShouldBeTrue();

            queryable.ToCommand(FetchType.FetchMany).CommandText.ShouldContain("inner_date = :p0", Case.Insensitive);
        }

        [Fact]
        public async Task Bug_1931_duplicated_deep_enum_field_with_string_storage()
        {
            StoreOptions(config =>
            {
                config.Schema.For<Application>()
                    .AddSubClassHierarchy(
                        typeof(ApplicationSubclass)
                    );

                config.Schema.For<Application>().GinIndexJsonData();

                config.Schema.For<Application>()
                    .Duplicate(a => a.ApplicationNumber)
                    .Duplicate(a => a.Status.StatusType);


                config.Schema.For<Application>().Identity(a => a.Id).HiloSettings(new HiloSettings {MaxLo = 100});

                config.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;
                config.Advanced.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime = false;
            });

            await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

            await theStore.BulkInsertDocumentsAsync(new Application[]
            {
                new Application
                {
                    Status = new Status {StatusType = StatusType.Unknown},
                    ApplicationNumber = "foo"
                },
                new Application()
            });

            await theStore.BulkInsertDocumentsAsync(new Application[]
            {
                new Application {Status = new Status {StatusType = StatusType.Unknown}},
                new Application()
            });

            var app = await theSession.Query<Application>().FirstOrDefaultAsync();
        }

        [Fact]
        public async Task Bug_1931_duplicated_deep_enum_field_with_int_storage()
        {
            StoreOptions(config =>
            {
                config.Schema.For<Application>()
                    .AddSubClassHierarchy(
                        typeof(ApplicationSubclass)
                    );

                config.Schema.For<Application>().GinIndexJsonData();

                config.Schema.For<Application>()
                    .Duplicate(a => a.ApplicationNumber)
                    .Duplicate(a => a.Status.StatusType);


                config.Schema.For<Application>().Identity(a => a.Id).HiloSettings(new HiloSettings {MaxLo = 100});

                config.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsInteger;
                config.Advanced.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime = false;
            });

            await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

            var app = await theSession.Query<Application>().FirstOrDefaultAsync();

            await theStore.BulkInsertDocumentsAsync(new Application[]
            {
                new Application {Status = new Status {StatusType = StatusType.Unknown}},
                new Application()
            });
            await theStore.BulkInsertDocumentsAsync(new Application[]
            {
                new Application {Status = new Status {StatusType = StatusType.Unknown}},
                new Application()
            });
        }

        [PropertySearching(PropertySearching.JSON_Locator_Only)]
        public class Organization
        {
            public Guid Id { get; set; }

            [DuplicateField]
            public string Name { get; set; }

            [DuplicateField(IndexMethod = IndexMethod.hash, IndexName = "idx_foo")]
            public string OtherName;

            [DuplicateField(IndexSortOrder = SortOrder.Desc)]
            public string YetAnotherName { get; set; }

            [DuplicateField(PgType = "timestamp")]
            public DateTime Time { get; set; }

            [DuplicateField]
            public DateTime Time2 { get; set; }

            public string OtherProp;
            public string OtherField { get; set; }
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


    public class ApplicationSubclass : Application
    {

        //------------------------------------

        public string SomeOtherProperty { get; set; }

        //------------------------------------

    }

    public class Status
    {

        //------------------------------------

        public DateTime Date { get; set; }
        public StatusType StatusType { get; set; }

        //------------------------------------

    }

    public enum StatusType
    {

        //------------------------------------

        Unknown = 0,
        StatusOne = 1,
        StatusTwo = 2,

        //------------------------------------

    }

    public class Application
    {

        //------------------------------------

        public long Id { get; set; }
        public string ApplicationNumber { get; set; }
        public Status Status { get; set; }

        //------------------------------------

    }
}
