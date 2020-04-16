using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Util
{
    public class NewTests
    {
        [Fact]
        public void can_create_intialized_instance_of_class_with_public_default_constructor()
        {
            var instance = New<ClassWithPublicDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<ClassWithPublicDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
        }

        [Fact]
        public void can_create_intialized_instance_of_class_with_public_default_constructor_and_public_non_default_constructor()
        {
            var instance = New<ClassWithPublicDefaultConstructorAndPublicNonDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<ClassWithPublicDefaultConstructorAndPublicNonDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
        }

        [Fact]
        public void can_create_intialized_instance_of_class_with_protected_default_constructor()
        {
            var instance = New<ClassWithProtectedDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<ClassWithProtectedDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
        }

        [Fact]
        public void can_create_intialized_instance_of_class_with_protected_default_constructor_and_public_non_default_constructor()
        {
            var instance = New<ClassWithProtectedDefaultConstructorAndPublicNonDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<ClassWithProtectedDefaultConstructorAndPublicNonDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
        }

        [Fact]
        public void can_create_intialized_instance_of_class_with_private_default_constructor()
        {
            var instance = New<ClassWithPrivateDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<ClassWithPrivateDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
        }

        [Fact]
        public void can_create_intialized_instance_of_class_with_private_default_constructor_and_public_non_default_constructor()
        {
            var instance = New<ClassWithPrivateDefaultConstructorAndPublicNonDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<ClassWithPrivateDefaultConstructorAndPublicNonDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
        }

        [Fact]
        public void can_create_intialized_instance_of_class_with_public_default_constructor_and_base_class_with_public_default_constructor()
        {
            var instance = New<DerivedClassWithPublicDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<DerivedClassWithPublicDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
            instance.IsInitializedInBaseClass.ShouldBeTrue();
        }

        [Fact]
        public void can_create_intialized_instance_of_class_with_protected_default_constructor_and_base_class_with_public_default_constructor()
        {
            var instance = New<DerivedClassWithProtectedDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<DerivedClassWithProtectedDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
            instance.IsInitializedInBaseClass.ShouldBeTrue();
        }

        [Fact]
        public void can_create_intialized_instance_of_class_with_private_default_constructor_and_base_class_with_public_default_constructor()
        {
            var instance = New<DerivedClassWithPrivateDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<DerivedClassWithPrivateDefaultConstructor>();
            instance.IsInitialized.ShouldBeTrue();
            instance.IsInitializedInBaseClass.ShouldBeTrue();
        }

        [Fact]
        public void can_create__not_intialized_instance_of_class_with_no_default_constructor_and_base_class_with_public_default_constructor()
        {
            var instance = New<DerivedClassWithNoDefaultConstructor>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeOfType<DerivedClassWithNoDefaultConstructor>();
            instance.IsInitialized.ShouldBeFalse();
            instance.IsInitializedInBaseClass.ShouldBeFalse();
        }

        [Fact]
        public void can_create_intialized_instance_of_string_and_returns_empty_string()
        {
            var instance = New<string>.Instance();

            instance.ShouldNotBeNull();
            instance.ShouldBeEmpty();
        }
    }

    internal class ClassWithPublicDefaultConstructor
    {
        public bool IsInitialized = false;

        public ClassWithPublicDefaultConstructor()
        {
            IsInitialized = true;
        }
    }

    internal class ClassWithPublicDefaultConstructorAndPublicNonDefaultConstructor
    {
        public bool IsInitialized = false;

        public ClassWithPublicDefaultConstructorAndPublicNonDefaultConstructor()
        {
            IsInitialized = true;
        }

        public ClassWithPublicDefaultConstructorAndPublicNonDefaultConstructor(string stringParam)
        {
        }
    }

    internal class ClassWithProtectedDefaultConstructor
    {
        public bool IsInitialized = true;

        private ClassWithProtectedDefaultConstructor()
        {
        }
    }

    internal class ClassWithProtectedDefaultConstructorAndPublicNonDefaultConstructor
    {
        public bool IsInitialized = false;

        private ClassWithProtectedDefaultConstructorAndPublicNonDefaultConstructor()
        {
            IsInitialized = true;
        }

        public ClassWithProtectedDefaultConstructorAndPublicNonDefaultConstructor(string stringParam)
        {
        }
    }

    internal class ClassWithPrivateDefaultConstructor
    {
        public bool IsInitialized = false;

        private ClassWithPrivateDefaultConstructor()
        {
            IsInitialized = true;
        }
    }

    internal class ClassWithPrivateDefaultConstructorAndPublicNonDefaultConstructor
    {
        public bool IsInitialized = false;

        private ClassWithPrivateDefaultConstructorAndPublicNonDefaultConstructor()
        {
            IsInitialized = true;
        }

        public ClassWithPrivateDefaultConstructorAndPublicNonDefaultConstructor(string stringParam)
        {
        }
    }

    internal class BaseClassWithPublicDefaultConstructor
    {
        public bool IsInitializedInBaseClass = false;

        public BaseClassWithPublicDefaultConstructor()
        {
            IsInitializedInBaseClass = true;
        }
    }

    internal class DerivedClassWithPublicDefaultConstructor: BaseClassWithPublicDefaultConstructor
    {
        public bool IsInitialized = false;

        public DerivedClassWithPublicDefaultConstructor()
        {
            IsInitialized = true;
        }
    }

    internal class DerivedClassWithProtectedDefaultConstructor: BaseClassWithPublicDefaultConstructor
    {
        public bool IsInitialized = false;

        private DerivedClassWithProtectedDefaultConstructor()
        {
            IsInitialized = true;
        }
    }

    internal class DerivedClassWithPrivateDefaultConstructor: BaseClassWithPublicDefaultConstructor
    {
        public bool IsInitialized = false;

        private DerivedClassWithPrivateDefaultConstructor()
        {
            IsInitialized = true;
        }
    }

    internal class DerivedClassWithNoDefaultConstructor: BaseClassWithPublicDefaultConstructor
    {
        public bool IsInitialized = false;

        private DerivedClassWithNoDefaultConstructor(string stringParam)
        {
            IsInitialized = true;
        }
    }
}
