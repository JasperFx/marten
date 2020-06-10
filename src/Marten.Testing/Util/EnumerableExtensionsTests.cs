using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Testing.Harness;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Util
{
    public class EnumerableExtensionsTests
    {
        [Fact]
        public void should_sort()
        {
            var dependencies = new Dictionary<int, int[]>
            {
                {4, new[] {3}},
                {1, new int[0]},
                {3, new[] {2}},
                {5, new[] {4}},
                {8, new[] {7}},
                {6, new[] {5}},
                {9, new[] {8}},
                {7, new[] {6}},
                {2, new[] {1}}
            };

            dependencies.Keys.TopologicalSort(x => dependencies[x]).ToArray().ShouldBe(new[] {1, 2, 3, 4, 5, 6, 7, 8, 9});
        }

        [Fact]
        public void should_throw_on_cycle()
        {
            var dependencies = new Dictionary<int, int[]>
            {
                {4, new[] {3}},
                {1, new[] {9}},
                {3, new[] {2}},
                {5, new[] {4}},
                {8, new[] {7}},
                {6, new[] {5}},
                {9, new[] {8}},
                {7, new[] {6}},
                {2, new[] {1}}
            };

            Exception<Exception>.ShouldBeThrownBy(() =>
            {
                dependencies.Keys.TopologicalSort(x => dependencies[x]);
            });
        }

        [Fact]
        public void should_not_throw_on_cycle()
        {
            var dependencies = new Dictionary<int, int[]>
            {
                {4, new[] {3}},
                {1, new[] {9}},
                {3, new[] {2}},
                {5, new[] {4}},
                {8, new[] {7}},
                {6, new[] {5}},
                {9, new[] {8}},
                {7, new[] {6}},
                {2, new[] {1}}
            };

            dependencies.Keys.TopologicalSort(x => dependencies[x], false);
        }
    }
}
