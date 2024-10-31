#nullable enable
using Marten.Internal.Operations;
using Weasel.Core.Operations;

namespace Marten.Services;

public interface IDeletion: IStorageOperation, NoDataReturnedCall
{
    object Document { get; set; }
    object Id { get; set; }
}
