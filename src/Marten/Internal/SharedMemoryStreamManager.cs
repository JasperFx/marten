using System.IO;
using Microsoft.IO;

namespace Marten.Internal;

public static class SharedMemoryStreamManager
{
    private static readonly RecyclableMemoryStreamManager Manager = new();

    public static MemoryStream GetStream()
    {
        return Manager.GetStream();
    }
}
