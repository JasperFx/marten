namespace Marten.Events.V4Concept
{
    internal interface IValidatedProjection
    {
        void AssertValidity();
    }
}
