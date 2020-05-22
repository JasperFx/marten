using System;
using System.Linq;
using System.Reflection;
using Baseline.Reflection;
using Marten.Schema.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing
{
    public class JsonLocatorFieldTests
    {
        public readonly JsonLocatorField theStringField = 
            JsonLocatorField.For<User>(EnumStorage.AsInteger, Casing.Default, x => x.FirstName);
        public readonly JsonLocatorField theNumberField = 
            JsonLocatorField.For<User>(EnumStorage.AsInteger, Casing.Default, x => x.Age);
        public readonly JsonLocatorField theEnumField = 
            JsonLocatorField.For<Target>(EnumStorage.AsInteger, Casing.Default, x => x.Color);


        [Fact]
        public void selection_locator_matches_sql_locator_for_non_dates()
        {
            theStringField.TypedLocator.ShouldBe(theStringField.RawLocator);
            theNumberField.TypedLocator.ShouldBe(theNumberField.RawLocator);
            theEnumField.TypedLocator.ShouldBe(theEnumField.RawLocator);
        }

        [Fact]
        public void member_name_is_derived()
        {
            theStringField.MemberName.ShouldBe("FirstName");
        }

        [Fact]
        public void has_the_member_path()
        {
            theStringField.Members.Single().ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe("FirstName");
        }

        [Theory]
        [InlineData(Casing.Default)]
        [InlineData(Casing.CamelCase)]
        public void locator_for_string(Casing casing)
        {
            var memberName = casing == Casing.Default ? "FirstName" : "firstName";

            JsonLocatorField.For<User>(EnumStorage.AsInteger, casing, x => x.FirstName)
                .TypedLocator.ShouldBe($"d.data ->> '{memberName}'");
        }

        [Fact]
        public void field_for_strings_or_number_are_not_containment()
        {
            theStringField.ShouldUseContainmentOperator().ShouldBeFalse();
            theNumberField.ShouldUseContainmentOperator().ShouldBeFalse();
        }

        [Fact]
        public void make_datetime_fields_be_containment()
        {
            JsonLocatorField.For<Target>(EnumStorage.AsInteger, Casing.Default, x => x.Date)
                .ShouldUseContainmentOperator().ShouldBeTrue();

            JsonLocatorField.For<Target>(EnumStorage.AsInteger, Casing.Default, x => x.DateOffset)
                .ShouldUseContainmentOperator().ShouldBeTrue();
        }

        [Theory]
        [InlineData(Casing.Default, "Age")]
        [InlineData(Casing.CamelCase, "age")]
        [InlineData(Casing.SnakeCase, "age")]
        public void locator_for_number(Casing casing, string memberName)
        {
            JsonLocatorField.For<User>(EnumStorage.AsInteger, casing, x => x.Age)
                .TypedLocator.ShouldBe($"CAST(d.data ->> '{memberName}' as integer)");
        }

        [Theory]
        [InlineData(Casing.Default, "Color")]
        [InlineData(Casing.CamelCase, "color")]
        [InlineData(Casing.SnakeCase, "color")]
        public void locator_for_enum_in_integer_mode(Casing casing, string memberName)
        {
            JsonLocatorField.For<Target>(EnumStorage.AsInteger, casing, x => x.Color)
                .TypedLocator.ShouldBe($"CAST(d.data ->> '{memberName}' as integer)");
        }

        [Theory]
        [InlineData(Casing.Default, "Color")]
        [InlineData(Casing.CamelCase, "color")]
        [InlineData(Casing.SnakeCase, "color")]
        public void locator_for_enum_in_string_mode(Casing casing, string memberName)
        {
            JsonLocatorField.For<Target>(EnumStorage.AsString, casing, x => x.Color)
                .TypedLocator.ShouldBe($"d.data ->> '{memberName}'");
        }


        [Theory]
        [InlineData(Casing.Default, "Inner", "Number")]
        [InlineData(Casing.CamelCase, "inner", "number")]
        [InlineData(Casing.SnakeCase, "inner", "number")]
        public void two_deep_members_json_locator(Casing casing, string innerName, string numberName)
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var number = ReflectionHelper.GetProperty<Target>(x => x.Number);

            var twodeep = new JsonLocatorField("d.data", new StoreOptions(), EnumStorage.AsInteger, casing, new MemberInfo[] {inner, number});

            twodeep.TypedLocator.ShouldBe($"CAST(d.data -> '{innerName}' ->> '{numberName}' as integer)");
        }


        [Theory]
        [InlineData(Casing.Default, "Inner", "Number")]
        [InlineData(Casing.CamelCase, "inner", "number")]
        [InlineData(Casing.SnakeCase, "inner", "number")]
        public void three_deep_members_json_locator(Casing casing, string innerName, string numberName)
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var number = ReflectionHelper.GetProperty<Target>(x => x.Number);

            var deep = new JsonLocatorField("d.data", new StoreOptions(), EnumStorage.AsInteger, casing, new MemberInfo[] { inner, inner, number });

            deep.TypedLocator.ShouldBe($"CAST(d.data -> '{innerName}' -> '{innerName}' ->> '{numberName}' as integer)");
        }

        [Theory]
        [InlineData(Casing.Default, "Inner", "String")]
        [InlineData(Casing.CamelCase, "inner", "string")]
        [InlineData(Casing.SnakeCase, "inner", "string")]
        public void three_deep_members_json_locator_for_string_property(Casing casing, string innerName, string stringName)
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var stringProp = ReflectionHelper.GetProperty<Target>(x => x.String);

            var deep = new JsonLocatorField("d.data", new StoreOptions(), EnumStorage.AsInteger, casing, new MemberInfo[] { inner, inner, stringProp });

            deep.TypedLocator.ShouldBe($"d.data -> '{innerName}' -> '{innerName}' ->> '{stringName}'");
        }

        public class DocWithDates
        {
            public Guid Id = Guid.NewGuid();

            public DateTime DateTime { get; set; }
            public DateTimeOffset DateTimeOffset { get; set; }

            public DateTime? NullableDateTime { get; set; }
            public DateTimeOffset? NullableDateTimeOffset { get; set; }
        }

        [Theory]
        [InlineData(Casing.Default, "DateTime", "NullableDateTime", "DateTimeOffset", "NullableDateTimeOffset")]
        [InlineData(Casing.CamelCase, "dateTime", "nullableDateTime", "dateTimeOffset", "nullableDateTimeOffset")]
        [InlineData(Casing.SnakeCase, "date_time", "nullable_date_time", "date_time_offset", "nullable_date_time_offset")]
        public void do_not_use_timestamp_functions_on_selection_locator_for_dates(Casing casing, string dateTimeName, string nullableDateTimeName, string dateTimeOffsetName, string nullableDateTimeOffsetName)
        {
            JsonLocatorField.For<DocWithDates>(EnumStorage.AsString, casing, x => x.DateTime)
                .RawLocator.ShouldBe($"CAST(d.data ->> '{dateTimeName}' as timestamp without time zone)");
            
            JsonLocatorField.For<DocWithDates>(EnumStorage.AsString, casing, x => x.NullableDateTime)
                .RawLocator.ShouldBe($"CAST(d.data ->> '{nullableDateTimeName}' as timestamp without time zone)");
            
            JsonLocatorField.For<DocWithDates>(EnumStorage.AsString, casing, x => x.DateTimeOffset)
                .RawLocator.ShouldBe($"CAST(d.data ->> '{dateTimeOffsetName}' as timestamp with time zone)");

            JsonLocatorField.For<DocWithDates>(EnumStorage.AsString, casing, x => x.NullableDateTimeOffset)
                .RawLocator.ShouldBe($"CAST(d.data ->> '{nullableDateTimeOffsetName}' as timestamp with time zone)");
                
        }
    }
}
