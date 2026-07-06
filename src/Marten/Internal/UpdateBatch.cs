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
    private readonly IReadOnlyList<Weasel.Storage.IStorageOperation> _operations;

    public UpdateBatch(IReadOnlyList<Weasel.Storage.IStorageOperation> operations)
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
        // 9.0 (#4375): single-page fast path avoids allocating the iterator state machine
        // + intermediate List<OperationPage> for the common single-batch SaveChanges case.
        if (_operations.Count == 0)
        {
            return Array.Empty<OperationPage>();
        }

        if (_operations.Count < ((IMartenSession)session).Options.UpdateBatchSize)
        {
            return new[] { new OperationPage(session, _operations) };
        }

        return buildMultiplePages(session);
    }

    private List<OperationPage> buildMultiplePages(IMartenSession session)
    {
        var batchSize = ((IMartenSession)session).Options.UpdateBatchSize;
        var pageCount = (_operations.Count + batchSize - 1) / batchSize;
        var pages = new List<OperationPage>(pageCount);

        var count = 0;
        while (count < _operations.Count)
        {
            var remaining = Math.Min(batchSize, _operations.Count - count);
            var operations = new Weasel.Storage.IStorageOperation[remaining];
            for (int i = 0; i < remaining; i++)
            {
                operations[i] = _operations[count + i];
            }

            pages.Add(new OperationPage(session, operations));

            count += batchSize;
        }

        return pages;
    }
}
