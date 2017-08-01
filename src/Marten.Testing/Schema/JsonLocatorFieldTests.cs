using System;
using System.Linq;
using System.Reflection;
using Baseline.Reflection;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
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
            theStringField.SqlLocator.ShouldBe(theStringField.SelectionLocator);
            theNumberField.SqlLocator.ShouldBe(theNumberField.SelectionLocator);
            theEnumField.SqlLocator.ShouldBe(theEnumField.SelectionLocator);
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
                .SqlLocator.ShouldBe($"d.data ->> '{memberName}'");
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
        [InlineData(Casing.Default)]
        [InlineData(Casing.CamelCase)]
        public void locator_for_number(Casing casing)
        {
            var memberName = casing == Casing.Default ? "Age" : "age";

            JsonLocatorField.For<User>(EnumStorage.AsInteger, casing, x => x.Age)
                .SqlLocator.ShouldBe($"CAST(d.data ->> '{memberName}' as integer)");
        }

        [Theory]
        [InlineData(Casing.Default)]
        [InlineData(Casing.CamelCase)]
        public void locator_for_enum_in_integer_mode(Casing casing)
        {
            var memberName = casing == Casing.Default ? "Color" : "color";

            JsonLocatorField.For<Target>(EnumStorage.AsInteger, casing, x => x.Color)
                .SqlLocator.ShouldBe($"CAST(d.data ->> '{memberName}' as integer)");
        }

        [Theory]
        [InlineData(Casing.Default)]
        [InlineData(Casing.CamelCase)]
        public void locator_for_enum_in_string_mode(Casing casing)
        {
            var memberName = casing == Casing.Default ? "Color" : "color";

            JsonLocatorField.For<Target>(EnumStorage.AsString, casing, x => x.Color)
                .SqlLocator.ShouldBe($"d.data ->> '{memberName}'");
        }


        [Theory]
        [InlineData(Casing.Default)]
        [InlineData(Casing.CamelCase)]
        public void two_deep_members_json_locator(Casing casing)
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var number = ReflectionHelper.GetProperty<Target>(x => x.Number);
            var innerName = casing == Casing.Default ? "Inner" : "inner";
            var numberName = casing == Casing.Default ? "Number" : "number";

            var twodeep = new JsonLocatorField("d.data", EnumStorage.AsInteger, casing, new MemberInfo[] {inner, number});

            twodeep.SqlLocator.ShouldBe($"CAST(d.data -> '{innerName}' ->> '{numberName}' as integer)");
        }


        [Theory]
        [InlineData(Casing.Default)]
        [InlineData(Casing.CamelCase)]
        public void three_deep_members_json_locator(Casing casing)
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var number = ReflectionHelper.GetProperty<Target>(x => x.Number);
            var innerName = casing == Casing.Default ? "Inner" : "inner";
            var numberName = casing == Casing.Default ? "Number" : "number";

            var deep = new JsonLocatorField("d.data", EnumStorage.AsInteger, casing, new MemberInfo[] { inner, inner, number });

            deep.SqlLocator.ShouldBe($"CAST(d.data -> '{innerName}' -> '{innerName}' ->> '{numberName}' as integer)");
        }

        [Theory]
        [InlineData(Casing.Default)]
        [InlineData(Casing.CamelCase)]
        public void three_deep_members_json_locator_for_string_property(Casing casing)
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var stringProp = ReflectionHelper.GetProperty<Target>(x => x.String);
            var innerName = casing == Casing.Default ? "Inner" : "inner";
            var stringName = casing == Casing.Default ? "String" : "string";

            var deep = new JsonLocatorField("d.data", EnumStorage.AsInteger, casing, new MemberInfo[] { inner, inner, stringProp });

            deep.SqlLocator.ShouldBe($"d.data -> '{innerName}' -> '{innerName}' ->> '{stringName}'");
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
        [InlineData(Casing.Default)]
        [InlineData(Casing.CamelCase)]
        public void do_not_use_timestamp_functions_on_selection_locator_for_dates(Casing casing)
        {
            var name = casing == Casing.Default ? "DateTime" : "dateTime";

            JsonLocatorField.For<DocWithDates>(EnumStorage.AsString, casing, x => x.DateTime)
                .SelectionLocator.ShouldBe($"CAST(d.data ->> '{name}' as timestamp without time zone)");
            
            name = casing == Casing.Default ? "NullableDateTime" : "nullableDateTime";
            JsonLocatorField.For<DocWithDates>(EnumStorage.AsString, casing, x => x.NullableDateTime)
                .SelectionLocator.ShouldBe($"CAST(d.data ->> '{name}' as timestamp without time zone)");
            
            name = casing == Casing.Default ? "DateTimeOffset" : "dateTimeOffset";
            JsonLocatorField.For<DocWithDates>(EnumStorage.AsString, casing, x => x.DateTimeOffset)
                .SelectionLocator.ShouldBe($"CAST(d.data ->> '{name}' as timestamp with time zone)");

            name = casing == Casing.Default ? "NullableDateTimeOffset" : "nullableDateTimeOffset";
            JsonLocatorField.For<DocWithDates>(EnumStorage.AsString, casing, x => x.NullableDateTimeOffset)
                .SelectionLocator.ShouldBe($"CAST(d.data ->> '{name}' as timestamp with time zone)");
                
        }
    }
}