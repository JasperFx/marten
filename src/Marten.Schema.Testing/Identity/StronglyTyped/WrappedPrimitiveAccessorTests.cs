using System;
using Marten.Schema.Identity.StronglyTyped;
using Xunit;

namespace Marten.Schema.Testing.Identity.StronglyTyped
{
    public class WrappedPrimitiveAccessorTests
    {
        [Fact]
        public void GetCastableId()
        {
            var wrappedPrimitiveAccessor = new DefaultWrappedPrimitiveAccessor<ImplicitWrapper, Guid>();
            var id = Guid.NewGuid();
            var wrapper = new ImplicitWrapper(id);
            var extractedId = wrappedPrimitiveAccessor.GetId(wrapper);
            Assert.Equal(id, extractedId);
        }

        [Fact]
        public void GetWrapperWithIdField()
        {
            var wrappedPrimitiveAccessor = new DefaultWrappedPrimitiveAccessor<FieldWrapper, Guid>();
            var id = Guid.NewGuid();
            var wrapper = new FieldWrapper(id);
            var extractedId = wrappedPrimitiveAccessor.GetId(wrapper);
            Assert.Equal(id, extractedId);
        }

        [Fact]
        public void GetWrapperWithIdProperty()
        {
            var wrappedPrimitiveAccessor = new DefaultWrappedPrimitiveAccessor<PropertyWrapper, Guid>();
            var id = Guid.NewGuid();
            var wrapper = new PropertyWrapper(id);
            var extractedId = wrappedPrimitiveAccessor.GetId(wrapper);
            Assert.Equal(id, extractedId);
        }

        [Fact]
        public void NewSameType()
        {
            var wrappedPrimitiveAccessor = new DefaultWrappedPrimitiveAccessor<Guid, Guid>();
            var id = Guid.NewGuid();
            var wrapped = wrappedPrimitiveAccessor.NewId(id);
            Assert.Equal(id, wrapped);
        }

        [Fact]
        public void NewCastable()
        {
            var wrappedPrimitiveAccessor = new DefaultWrappedPrimitiveAccessor<ImplicitWrapper, Guid>();
            var id = Guid.NewGuid();
            var wrapped = wrappedPrimitiveAccessor.NewId(id);
            Assert.Equal(id, (Guid)wrapped);
        }

        [Fact]
        public void NewSingleParamConstructor()
        {
            var wrappedPrimitiveAccessor = new DefaultWrappedPrimitiveAccessor<PropertyWrapper, Guid>();
            var id = Guid.NewGuid();
            var wrapped = wrappedPrimitiveAccessor.NewId(id);
            Assert.Equal(id, wrapped.Id);
        }

        [Fact]
        public void NewDefaultConstructor()
        {
            var wrappedPrimitiveAccessor = new DefaultWrappedPrimitiveAccessor<DefaultWrapper, Guid>();
            var id = Guid.NewGuid();
            var wrapped = wrappedPrimitiveAccessor.NewId(id);
            Assert.Equal(id, wrapped.Id);
        }

        public class ImplicitWrapper
        {
            private Guid theWrappedId;

            public ImplicitWrapper(Guid id)
            {
                theWrappedId = id;
            }

            public static explicit operator Guid(ImplicitWrapper w) => w.theWrappedId;
            public static explicit operator ImplicitWrapper(Guid g) => new ImplicitWrapper(g);
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
