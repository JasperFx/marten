using System;
using Marten.Schema;

namespace Marten.Testing.Documents;

[SingleTenanted]
public class SingleTenantedDocument
{
    public Guid Id { get; set; }
}
