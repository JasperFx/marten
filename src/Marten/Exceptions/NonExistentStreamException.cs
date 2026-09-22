namespace Marten.Exceptions;

public class NonExistentStreamException: MartenException
{
    public NonExistentStreamException(object id): base(
        $"Attempt to append to a nonexistent event stream '{id}'. AppendOptimistic and AppendExclusive require the stream to exist in the session's tenant; call StartStream first, or use plain Append, which starts the stream when it is missing. With UseMandatoryStreamTypeDeclaration enabled, plain Append also requires the stream to have been started with a type.")
    {
        Id = id;
    }

    public object Id { get; }
}
