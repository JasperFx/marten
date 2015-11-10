namespace Marten.Schema.Sequences
{
    public interface ISequence
    {
        int NextInt();
        long NextLong();
    }
}