using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;

namespace Marten.Internal;

public class UpdateBatch: IUpdateBatch
{
    private readonly IList<Exception> _exceptions = new List<Exception>();
    private readonly IReadOnlyList<IStorageOperation> _operations;

    public UpdateBatch(IReadOnlyList<IStorageOperation> operations)
    {
        _operations = operations;
    }

    public string? TenantId { get; set; }

    public IReadOnlyList<Type> DocumentTypes()
    {
        return _operations.Select(x => x.DocumentType).Where(x => x != null).Distinct().ToList();
    }

    public Task PostUpdateAsync(IMartenSession session)
    {
        return Task.CompletedTask;
    }

    public Task PreUpdateAsync(IMartenSession session)
    {
        return Task.CompletedTask;
    }

    public IReadOnlyList<ITransactionParticipant> TransactionParticipants => [];

    public IReadOnlyList<OperationPage> BuildPages(IMartenSession session)
    {
        return buildPages(session).ToList();
    }

    private IEnumerable<OperationPage> buildPages(IMartenSession session)
    {
        if (!_operations.Any())
        {
            yield break;
        }

        if (_operations.Count < session.Options.UpdateBatchSize)
        {
            yield return new OperationPage(session, _operations);
        }
        else
        {
            var count = 0;
            var batchSize = session.Options.UpdateBatchSize;

            while (count < _operations.Count)
            {
                var remaining = Math.Min(batchSize, _operations.Count - count);
                var operations = new IStorageOperation[remaining];
                for (int i = 0; i < remaining; i++)
                {
                    operations[i] = _operations[count + i];
                }

                var page = new OperationPage(session, operations);
                yield return page;

                count += batchSize;
            }
        }
    }
}
