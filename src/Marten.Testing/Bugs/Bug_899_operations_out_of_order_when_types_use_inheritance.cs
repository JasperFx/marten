using System;
using Xunit;

namespace Marten.Testing.Bugs
{
	public class Bug_899_operations_out_of_order_when_types_use_inheritance : IntegratedFixture
	{
		[Fact]
		public void performs_soft_delete_then_store_operations_in_order()
		{
			StoreOptions(_ => _.Schema.For<IAccountingDocument>()
				.Identity(x => x.VersionId)
				.AddSubClassHierarchy()
				.SoftDeleted()
				// Create an index that only permits a single version of each document (where a document is 
				// 'identified' by DocumentId)
				.Index(x => x.DocumentId, x =>
				{
					x.IsUnique = true;
					x.Where = "mt_deleted = false";
				}));

			var docId = Guid.Parse("96d41d29-02a5-4c19-b019-034eb2cf964e");
			var doc = new Invoice { DocumentId = docId, VersionId = Guid.NewGuid(), Name = "Myergen" };

			using (var session = theStore.LightweightSession())
			{
				session.Store(doc);
				session.SaveChanges();
			}

			using (var session = theStore.LightweightSession())
			{
				session.Delete<Invoice>(doc.VersionId);

				// Create a new version of the document
				doc.VersionId = Guid.NewGuid();
				doc.Name = "Skimbleshanks";
				session.Store(doc);

				session.SaveChanges();
			}
		}
	}

	public interface IAccountingDocument
	{
		// Unique ID of a version of the document
		Guid VersionId { get; set; }

		// ID that remains the same for all versions of a document
		Guid DocumentId { get; set; }
	}

	// This bug only manifests when there are multiple implementations of BaseAccountingDocument
	public class Invoice : BaseAccountingDocument { }
	public class PurchaseOrder : BaseAccountingDocument { }

	// This bug only manifests when there are multiple levels of inheritance
	public abstract class BaseAccountingDocument : IAccountingDocument
	{
		public Guid VersionId { get; set; }
		public Guid DocumentId { get; set; }
		public string Name { get; set; }

		// This bug only manifests when there are multiple properties with non-builtin types
		public AnotherClass Another { get; set; }
		public YetAnotherClass YetAnother { get; set; }
	}

	public class AnotherClass { }

	public class YetAnotherClass { }
}
