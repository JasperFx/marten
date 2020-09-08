using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Fields;
using Marten.Services;
using Marten.Testing.Bugs;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Fields
{
    public class FieldCollectionTests
    {
        private StoreOptions theOptions = new StoreOptions();



        private IField fieldFor<T>(Expression<Func<T, object>> expression)
        {
            return new FieldMapping("d", typeof(T), theOptions).FieldFor(expression);
        }

        private IField fieldFor<T>(string memberName)
        {
            return new FieldMapping("d", typeof(T), theOptions).FieldFor(memberName);
        }

        public class FieldHolder
        {
            public string[] Array;
            public List<string> List;
            public IList<string> IList;
            public IReadOnlyList<string> IReadOnlyList;
            public ICollection<string> ICollection;
            public IEnumerable<string> IEnumerable;

        }

        [Theory]
        [InlineData(nameof(FieldHolder.Array))]
        [InlineData(nameof(FieldHolder.List))]
        [InlineData(nameof(FieldHolder.IList))]
        [InlineData(nameof(FieldHolder.IReadOnlyList))]
        [InlineData(nameof(FieldHolder.ICollection))]
        [InlineData(nameof(FieldHolder.IEnumerable))]
        public void find_array_field_for_collection_types(string memberName)
        {
            fieldFor<FieldHolder>(memberName).ShouldBeOfType<ArrayField>();
        }

        [Fact]
        public void enum_as_integer()
        {
            theOptions.Serializer(new JsonNetSerializer
            {
                EnumStorage = EnumStorage.AsInteger
            });

            fieldFor<Target>(x => x.Color).ShouldBeOfType<EnumAsIntegerField>();
        }

        [Fact]
        public void enum_as_string()
        {
            theOptions.Serializer(new JsonNetSerializer
            {
                EnumStorage = EnumStorage.AsString
            });

            fieldFor<Target>(x => x.Color).ShouldBeOfType<EnumAsStringField>();
        }

        [Fact]
        public void date_time()
        {
            fieldFor<Target>(x => x.Date).ShouldBeOfType<DateTimeField>();

        }

        [Fact]
        public void nullable_date_time()
        {
            fieldFor<Target>(x => x.NullableDateTime)
                .ShouldBeOfType<NullableTypeField>()
                .InnerField
                .ShouldBeOfType<DateTimeField>();
        }

        [Fact]
        public void datetime_offset()
        {
            fieldFor<Target>(x => x.DateOffset).ShouldBeOfType<DateTimeOffsetField>();
        }

        [Fact]
        public void string_field()
        {
            fieldFor<Target>(x => x.String).ShouldBeOfType<StringField>();
        }

        [Fact]
        public void simple_case_types()
        {
            fieldFor<Target>(x => x.Decimal).ShouldBeOfType<SimpleCastField>();
            fieldFor<Target>(x => x.Number).ShouldBeOfType<SimpleCastField>();
            fieldFor<Target>(x => x.Double).ShouldBeOfType<SimpleCastField>();
            fieldFor<Target>(x => x.Float).ShouldBeOfType<SimpleCastField>();
        }
    }
}
