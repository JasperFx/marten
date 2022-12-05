namespace Marten.Exceptions;

public class NonExistentStreamException: MartenException
{
    public NonExistentStreamException(object id): base($"Attempt to append to a nonexistent event stream '{id}'")
    {
        Id = id;
    }

    public object Id { get; }
}
