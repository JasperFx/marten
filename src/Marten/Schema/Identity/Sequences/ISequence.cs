#nullable enable
using System.Threading.Tasks;

namespace Marten.Schema.Identity.Sequences;

public interface ISequence
{
    int MaxLo { get; }
    int NextInt();

    long NextLong();

    Task SetFloor(long floor);
}
