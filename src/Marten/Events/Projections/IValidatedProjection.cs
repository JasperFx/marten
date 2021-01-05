namespace Marten.Events.Projections
{
    internal interface IValidatedProjection
    {
        void AssertValidity();
    }
}
