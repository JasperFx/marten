namespace Marten.Schema.Identity.Sequences
{
    public interface ISequence
    {
        int NextInt();
        long NextLong();
    }
}