using System.Linq;
using Marten.Map;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing
{
    public class DocumentMapTests
    {
        public void get_nonexisting_document()
        {
            var map = new DocumentMap(new JsonNetSerializer());
            var entry = map.Get<Company>("notexisting");
            entry.ShouldBeNull();
        }

        public void get_existing_document()
        {
            var map = new DocumentMap(new JsonNetSerializer());
            var document = new Company();
            map.Set("existing", document);

            var entry = map.Get<Company>("existing");

            entry.ShouldNotBeNull();
            entry.Id.Id.ShouldBe("existing");
            entry.Id.DocumentType.ShouldBe(typeof(Company));
            entry.Document.ShouldBeSameAs(document);
            entry.OriginalJson.ShouldBeNull();
        }

        public void get_existing_document_with_original_json()
        {
            var map = new DocumentMap(new JsonNetSerializer());
            var document = new Company();
            map.Set("existing", document, "{}");

            var entry = map.Get<Company>("existing");

            entry.ShouldNotBeNull();
            entry.Id.Id.ShouldBe("existing");
            entry.Id.DocumentType.ShouldBe(typeof(Company));
            entry.Document.ShouldBeSameAs(document);
            entry.OriginalJson.ShouldBe("{}");
        }

        public void get_updates_of_a_new_document()
        {
            var map = new DocumentMap(new JsonNetSerializer());
            var document = new Company();
            map.Set("existing", document);

            var updates = map.GetUpdates().ToArray();

            updates.Length.ShouldBe(1);
            updates[0].Document.ShouldBeSameAs(document);
            updates[0].Id.ShouldBe(new DocumentIdentity(typeof(Company), "existing"));
            updates[0].Json.ShouldBe($"{{\"Id\":\"{document.Id}\",\"Name\":null}}");
        }

        public void get_updates_of_an_updated_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new DocumentMap(serializer);
            var document = new Company();
            map.Set("existing", document, serializer.ToJson(document));

            document.Name = "newName";

            var updates = map.GetUpdates().ToArray();

            updates.Length.ShouldBe(1);
            updates[0].Document.ShouldBeSameAs(document);
            updates[0].Id.ShouldBe(new DocumentIdentity(typeof(Company), "existing"));
            updates[0].Json.ShouldBe($"{{\"Id\":\"{document.Id}\",\"Name\":\"newName\"}}");
        }

        public void get_updates_of_not_updated_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new DocumentMap(serializer);
            var document = new Company();
            map.Set("existing", document, serializer.ToJson(document));

            var updates = map.GetUpdates().ToArray();

            updates.Length.ShouldBe(0);
        }

        public void not_updating_an_updated_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new DocumentMap(serializer);
            var document = new Company();
            map.Set("existing", document, serializer.ToJson(document));

            document.Name = "new";

            var updates = map.GetUpdates().ToArray();
            updates.Length.ShouldBe(1);
            map.Updated(updates);

            updates = map.GetUpdates().ToArray();
            updates.Length.ShouldBe(0);
        }

        public void not_updating_an_updated_new_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new DocumentMap(serializer);
            var document = new Company();
            document.Name = "new";
            map.Set("existing", document);

            var updates = map.GetUpdates().ToArray();
            updates.Length.ShouldBe(1);
            map.Updated(updates);

            updates = map.GetUpdates().ToArray();
            updates.Length.ShouldBe(0);
        }

        public void updating_an_updated_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new DocumentMap(serializer);
            var document = new Company();
            map.Set("existing", document, serializer.ToJson(document));

            document.Name = "new";

            var updates = map.GetUpdates().ToArray();
            updates.Length.ShouldBe(1);
            map.Updated(updates);

            document.Name = "new2";

            updates = map.GetUpdates().ToArray();
            updates.Length.ShouldBe(1);
        }

        public void updating_an_updated_new_document()
        {
            var serializer = new JsonNetSerializer();
            var map = new DocumentMap(serializer);
            var document = new Company();
            document.Name = "new";
            map.Set("existing", document);

            var updates = map.GetUpdates().ToArray();
            updates.Length.ShouldBe(1);
            map.Updated(updates);

            document.Name = "new2";

            updates = map.GetUpdates().ToArray();
            updates.Length.ShouldBe(1);
        }
    }
}