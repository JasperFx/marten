using System;

namespace Marten.Schema.Identity;

public interface IIdentityRule
{
    bool TryMatch(Type type, out IIdGeneration innerType);
}

