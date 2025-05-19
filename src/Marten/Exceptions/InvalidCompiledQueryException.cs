using System;
using System.Runtime.Serialization;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;

namespace Marten.Exceptions;

public class InvalidCompiledQueryException: MartenException
{
    public static readonly string CompiledQueryTypeCannotBeAsyncMessage =
        "Invalid compiled query type `{0}`. Compiled queries cannot use asynchronous query selectors like 'CountAsync()'. Please use the synchronous equivalent like 'Count()' instead. You will still be able to query asynchronously through IQuerySession.QueryAsync().";

    public InvalidCompiledQueryException(string message): base(message)
    {
    }

    public InvalidCompiledQueryException(string message, Exception innerException): base(message, innerException)
    {
    }

    protected InvalidCompiledQueryException(SerializationInfo info, StreamingContext context): base(info, context)
    {
    }

    public static InvalidCompiledQueryException ForCannotBeAsync(Type compiledQueryType)
    {
        return new InvalidCompiledQueryException(string.Format(CompiledQueryTypeCannotBeAsyncMessage, compiledQueryType.FullNameInCode()));
    }
}
