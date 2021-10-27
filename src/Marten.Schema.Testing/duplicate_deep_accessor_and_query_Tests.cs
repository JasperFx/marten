using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Testing.Documents;
using Marten.Services;
using Marten.Testing.Acceptance;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace Marten.Schema.Testing
{
    public class duplicate_deep_accessor_and_query_Tests : IntegrationContext
    {
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

            queryable.ToCommand(FetchType.FetchMany).CommandText.ShouldContain("inner_date = :p0");
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

            theStore.BulkInsertDocuments(new Application[]
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
            theStore.BulkInsertDocuments(new Application[]
            {
                new Application {Status = new Status {StatusType = StatusType.Unknown}},
                new Application()
            });
        }


        //------------------------------------

        //------------------------------------

        //------------------------------------
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
