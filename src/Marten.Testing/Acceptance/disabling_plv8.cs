using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class disabling_plv8: IntegrationContext
    {
        [Fact]
        public void active_features_includes_transforms_with_plv8_enabled()
        {
            theStore.Storage.AllActiveFeatures(theStore.Tenancy.Default)
                    .Any(x => x is Marten.Transforms.Transforms).ShouldBeTrue();
        }

        [Fact]
        public void transforms_are_left_out_with_plv8_disabled()
        {
            StoreOptions(_ =>
            {
                _.PLV8Enabled = false;
            });

            theStore.Storage.AllActiveFeatures(theStore.Tenancy.Default)
                    .Any(x => x is Marten.Transforms.Transforms).ShouldBeFalse();
        }

        [Fact]
        public void get_invalid_operation_and_message_if_trying_to_use_patching_with_plv8_disabled()
        {
            StoreOptions(_ =>
            {
                _.PLV8Enabled = false;
            });

            using (var session = theStore.OpenSession())
            {
                SpecificationExtensions.ShouldContain(Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
                {
                    session.Patch<User>(Guid.NewGuid()).Set("foo", "bar");
                    session.SaveChanges();
                }).Message, "PLV8");
            }
        }

        [Fact]
        public void get_invalid_operation_and_message_if_trying_to_use_transforms_with_plv8_disabled()
        {
            StoreOptions(_ =>
            {
                _.PLV8Enabled = false;
            });

            SpecificationExtensions.ShouldContain(Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                theStore.Transform.All<User>("something");
            }).Message, "PLV8");
        }

        public disabling_plv8(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
