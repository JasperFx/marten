#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Weasel.Core.Operations;
using ICommandBuilder = Weasel.Postgresql.ICommandBuilder;

namespace Marten.Internal.Operations;

public interface IStorageOperation: IStorageOperation<ICommandBuilder, IMartenSession>
{

}
