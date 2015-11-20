using System.Linq;
using FubuCore;
using Marten.Map;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing.Map
{
    public class NonTrackedDocumentMapMapTests
    {
        public void get_nonexisting_document()
        {
            var map = new NonTrackedDocumentMap(new JsonNetSerializer());
            var entry = map.Get<Company>("notexisting");
            entry.ShouldBeNull();
        }

        public void get_stored_document()
        {
            var map = new NonTrackedDocumentMap(new JsonNetSerializer());
            var document = new Company();
            map.Store("existing", document);

            var entry = map.Get<Company>("existing");

            entry.ShouldBeNull();
        }

        public void get_loaded_document_with_original_json()
        {
            var map = new NonTrackedDocumentMap(new JsonNetSerializer());
            var document = new Company();
            map.Loaded("existing", document, "{}");

            var entry = map.Get<Company>("existing");

            entry.ShouldBeNull();
        }

        public void get_updates_of_a_new_document()
        {
            var map = new NonTrackedDocumentMap(new JsonNetSerializer());
            var document = new Company();
            map.Store("existing", document);

            var updates = map.GetChanges().ToArray();

            updates.Length.ShouldBe(1);
            var documentChange = updates[0].As<DocumentUpdate>();
            documentChange.ShouldNotBeNull();
            documentChange.Document.ShouldBeSameAs(document);
            documentChange.Id.ShouldBe(new DocumentIdentity(typeof(Company), "existing"));
            documentChange.Json.ShouldBe($"{{\"Id\":\"{document.Id}\",\"Name\":null}}");
        }

        public void get_updates_of_an_stored_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            map.Store("existing", document);

            document.Name = "newName";

            var updates = map.GetChanges().ToArray();

            updates.Length.ShouldBe(1);
            var documentChange = updates[0].As<DocumentUpdate>();
            documentChange.ShouldNotBeNull();
            documentChange.Document.ShouldBeSameAs(document);
            documentChange.Id.ShouldBe(new DocumentIdentity(typeof(Company), "existing"));
            documentChange.Json.ShouldBe($"{{\"Id\":\"{document.Id}\",\"Name\":\"newName\"}}");
        }

        public void get_updates_of_a_loaded_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            map.Loaded("existing", document, serializer.ToJson(document));

            document.Name = "newName";

            var updates = map.GetChanges().ToArray();

            updates.Length.ShouldBe(0);
        }

        public void get_updates_of_not_updated_stored_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            map.Store("existing", document);

            var updates = map.GetChanges().ToArray();

            updates.Length.ShouldBe(1);
        }

        public void get_updates_of_not_updated_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            map.Loaded("existing", document, serializer.ToJson(document));

            var updates = map.GetChanges().ToArray();

            updates.Length.ShouldBe(0);
        }

        public void not_updating_an_updated_a_stored_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            map.Store("existing", document);

            document.Name = "new";

            var updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(1);
            map.ChangesApplied(updates);

            updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(0);
        }

        public void not_updating_an_updated_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            map.Loaded("existing", document, serializer.ToJson(document));

            document.Name = "new";

            var updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(0);
            map.ChangesApplied(updates);
        }

        public void not_updating_an_updated_new_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            document.Name = "new";
            map.Store("existing", document);

            var updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(1);
            map.ChangesApplied(updates);

            updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(0);
        }

        public void updating_an_updated_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            map.Loaded("existing", document, serializer.ToJson(document));

            document.Name = "new";

            var updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(0);
            map.ChangesApplied(updates);

            document.Name = "new2";

            updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(0);
        }

        public void updating_an_updated_new_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company();
            document.Name = "new";
            map.Store("existing", document);

            var updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(1);
            map.ChangesApplied(updates);

            document.Name = "new2";

            updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(0);
        }

        public void deleting_a_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company {Name = "new"};
            map.DeleteDocument(document);

            var updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(1);
            map.ChangesApplied(updates);

            updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(0);
        }

        public void deleting_a_document_by_id()
        {
            var serializer = new JsonNetSerializer();
            var map = new NonTrackedDocumentMap(serializer);
            var document = new Company {Name = "new"};
            map.DeleteDocument(document);

            var updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(1);
            map.ChangesApplied(updates);

            updates = map.GetChanges().ToArray();
            updates.Length.ShouldBe(0);
        }
    }
}