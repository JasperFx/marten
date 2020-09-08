using Marten.Internal.Operations;

namespace Marten.Services
{


    public interface IDeletion: IStorageOperation, NoDataReturnedCall
    {
        object Document { get; set; }
        object Id { get; set; }
    }
}
