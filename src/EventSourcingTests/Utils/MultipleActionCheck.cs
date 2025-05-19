using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;

namespace EventSourcingTests.Utils;

[Obsolete("Gotta be a better way to do this")]
internal static class When
{

    public static MultipleActionCheck<T> CalledForEachAsync<T>(IEnumerable<T> elements, Func<T, int, Task> action)
    {
        return new MultipleActionCheck<T>(elements, action);
    }
}

internal class MultipleActionCheck<T>
{
    private readonly T[] elements;
    private readonly Func<T, int, Task> asyncAction;

    internal MultipleActionCheck(IEnumerable<T> elements, Func<T, int, Task> asyncAction)
    {
        this.elements = elements.ToArray();
        this.asyncAction = asyncAction;
    }

    public Task ShouldSucceedAsync()
    {
        return PerformActionAsync();
    }

    public async Task<Exception> ShouldThrowIfAsync(bool check)
    {
        if (!check)
        {
            await ShouldSucceedAsync();
            return null;
        }

        return await Should.ThrowAsync<Exception>(PerformActionAsync());
    }

    private async Task PerformActionAsync()
    {
        for (var i = 0; i < elements.Length; i++)
        {
            await asyncAction(elements[i], i);
        }
    }
}
