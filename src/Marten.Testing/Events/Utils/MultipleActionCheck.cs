using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;

namespace Marten.Testing.Events.Utils
{
    internal static class When
    {
        public static MultipleActionCheck<T> CalledForEach<T>(IEnumerable<T> elements, Action<T, int> action)
        {
            return new MultipleActionCheck<T>(elements, action);
        }

        public static MultipleActionCheck<T> CalledForEachAsync<T>(IEnumerable<T> elements, Func<T, int, Task> action)
        {
            return new MultipleActionCheck<T>(elements, action);
        }
    }

    internal class MultipleActionCheck<T>
    {
        private readonly T[] elements;
        private readonly Action<T, int> action;
        private readonly Func<T, int, Task> asyncAction;

        internal MultipleActionCheck(IEnumerable<T> elements, Action<T, int> action)
        {
            this.elements = elements.ToArray();
            this.action = action;
        }

        internal MultipleActionCheck(IEnumerable<T> elements, Func<T, int, Task> asyncAction)
        {
            this.elements = elements.ToArray();
            this.asyncAction = asyncAction;
        }

        public void ShouldSucceed()
        {
            Should.NotThrow(() => PerformAction());
        }

        public Task ShouldSucceedAsync()
        {
            return PerformActionAsync();
        }

        public Exception ShouldThrowIf(bool check)
        {
            if (!check)
            {
                ShouldSucceed();
                return null;
            }

            return Should.Throw<Exception>(() => PerformAction());
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

        private void PerformAction()
        {
            for (var i = 0; i < elements.Length; i++)
            {
                action(elements[i], i);
            }
        }

        private async Task PerformActionAsync()
        {
            for (var i = 0; i < elements.Length; i++)
            {
                await asyncAction(elements[i], i);
            }
        }
    }
}