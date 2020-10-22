using Marten.Schema.Identity.StronglyTyped;
using System;
using Xunit;

namespace Marten.Schema.Testing.Identity.StronglyTyped
{
    public class DefaultPrimitiveTypeFinderTests
    {
        [Fact]
        public void SingleParamConstructor()
        {
            var type = DefaultPrimitiveTypeFinder.FindPrimitiveType(typeof(FieldWrapper));
            Assert.Equal(typeof(Guid), type);
        }

        [Fact]
        public void DefaultConstructor()
        {
            var type = DefaultPrimitiveTypeFinder.FindPrimitiveType(typeof(DefaultWrapper));
            Assert.Equal(typeof(Guid), type);
        }

        [Fact]
        public void SingleParamPropConstructor()
        {
            var type = DefaultPrimitiveTypeFinder.FindPrimitiveType(typeof(PropertyWrapper));
            Assert.Equal(typeof(Guid), type);
        }

        public class FieldWrapper
        {
            private Guid id;

            public FieldWrapper(Guid id)
            {
                this.id = id;
            }
        }

        public class PropertyWrapper
        {
            public Guid Id { get; }

            public PropertyWrapper(Guid id)
            {
                this.Id = id;
            }
        }

        public class DefaultWrapper
        {
            public Guid Id { get; set; }
        }
    }
}
