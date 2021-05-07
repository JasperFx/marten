using System.Threading.Tasks;

#nullable enable
namespace Marten.Schema.Identity.Sequences
{
    public interface ISequence
    {
        int NextInt();

        long NextLong();

        int MaxLo { get; }

        Task SetFloor(long floor);
    }
}
