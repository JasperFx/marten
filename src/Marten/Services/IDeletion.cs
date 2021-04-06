using Marten.Internal.Operations;
#nullable enable
namespace Marten.Services
{
    public interface IDeletion: IStorageOperation, NoDataReturnedCall
    {
        object Document { get; set; }
        object Id { get; set; }
    }
}
