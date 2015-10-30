using System;
using System.Reflection;
using Marten.Schema;
using Shouldly;

namespace Marten.Testing.Schema
{
    public class DocumentMappingTests
    {
        public void pick_up_upper_case_property_id()
        {
            var mapping = new DocumentMapping(typeof(UpperCaseProperty));
            mapping.IdMember.ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe(nameof(UpperCaseProperty.Id));
        }

        public void pick_up_lower_case_property_id()
        {
            var mapping = new DocumentMapping(typeof(LowerCaseProperty));
            mapping.IdMember.ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe(nameof(LowerCaseProperty.id));
        }

        public void pick_up_lower_case_field_id()
        {
            var mapping = new DocumentMapping(typeof(LowerCaseField));
            mapping.IdMember.ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe(nameof(LowerCaseField.id));
        }

        public void pick_up_upper_case_field_id()
        {
            var mapping = new DocumentMapping(typeof(UpperCaseField));
            mapping.IdMember.ShouldBeAssignableTo<FieldInfo>()
                .Name.ShouldBe(nameof(UpperCaseField.Id));
        }

        public class UpperCaseProperty
        {
            public Guid Id { get; set; }
        }

        public class LowerCaseProperty
        {
            public Guid id { get; set; }
        }

        public class UpperCaseField
        {
            public int Id;
        }

        public class LowerCaseField
        {
            public int id;
        }
    }
}