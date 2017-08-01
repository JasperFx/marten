using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Xunit;

namespace Marten.Testing.Examples
{
    public sealed class Policies
    {
        class MultiTenantType : IRequireMultiTenancy
        {
            public Guid Id { get; set; }
        }

        [Fact]
        public void SetTenancyThroughPolicy()
        {
            // SAMPLE: sample-policy-configure
            var store = DocumentStore.For(storeOptions =>
            {                
                // Apply custom policy
                storeOptions.Policies.OnDocuments<TenancyPolicy>();                
            // ENDSAMPLE
                storeOptions.Connection("");
            });

            Assert.Equal(TenancyStyle.Conjoined, store.Storage.MappingFor(typeof(MultiTenantType)).TenancyStyle);
        }
        // SAMPLE: sample-policy-implementation
        public interface IRequireMultiTenancy
        {
        }
        public class TenancyPolicy : IDocumentPolicy
        {
            public void Apply(DocumentMapping mapping)
            {
                if (mapping.DocumentType.GetInterfaces().Any(x => x == typeof(IRequireMultiTenancy)))
                {
                    mapping.TenancyStyle = TenancyStyle.Conjoined;
                }
            }
        }
        // ENDSAMPLE
    }
}
