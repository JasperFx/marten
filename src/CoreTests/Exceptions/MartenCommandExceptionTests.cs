using System;
using Marten.Exceptions;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

public class MartenCommandExceptionTests
{
    [Fact]
    public void should_create_MartenCommandException_when_command_is_null()
    {
        var createWithNullCommand = () => new MartenCommandException(null, new Exception());

        createWithNullCommand.ShouldNotThrow();
    }
}
