namespace Marten.Schema.Identity.Sequences
{
    public class HiloSettings
    {
        public int MaxLo = 1000;
        public string SequenceName = null;
        public int MaxAdvanceToNextHiAttempts = 30;
    }
}
