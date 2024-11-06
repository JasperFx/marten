#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Internal.Operations;

public interface IStorageOperation: IStorageOperation<ICommandBuilder, IMartenSession>
{

}
