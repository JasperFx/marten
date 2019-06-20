namespace Marten.Schema.Identity.Sequences
{
    public interface ISequence
    {
        int NextInt();

        long NextLong();

        int MaxLo { get; }

        void SetFloor(long floor);
    }
}
