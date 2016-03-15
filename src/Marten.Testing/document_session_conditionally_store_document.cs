using System;
using Marten.Services;
using Xunit;

namespace Marten.Testing
{
    public class TypeToPersist
    {
        public Guid Id = Guid.NewGuid();

        public string Value { get; set; }
    }
    public class document_session_conditionally_store_document : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void can_persist_and_load_generic_types()
        {
            var doc = new TypeToPersist() { Value = "Before update" };
            
            theSession.Store(doc);
            theSession.SaveChanges();

            var docToUpdate = theSession.Load<TypeToPersist>(doc.Id);
            docToUpdate.Value = "After update";

            theSession.Store(docToUpdate, d => d.Value == "Nonexistent");
            theSession.SaveChanges();

            var docAfterZeroMatchUpdate = theSession.Load<TypeToPersist>(doc.Id);

            theSession.Store(docToUpdate, d => d.Value == "Before update");
            theSession.SaveChanges();

            var docAfterMatchingUpdate = theSession.Load<TypeToPersist>(doc.Id);

            Assert.NotEqual(docToUpdate.Value, docAfterZeroMatchUpdate.Value);
            Assert.Equal(docToUpdate.Value, docAfterMatchingUpdate.Value);
        }
    }
}