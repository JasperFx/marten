using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline.Reflection;
using Marten.Linq.Compiled;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Compiled
{
    public class CompiledQueryMemberExpressionVisitorTests
    {
        public class FakeThing
        {
            public bool IsOkay()
            {
                return true;
            }
        }

        private bool isContainment<T>(Expression<Action<T>> expression)
        {
            var method = ReflectionHelper.GetMethod(expression);
            return CompiledQueryMemberExpressionVisitor.IsContainmentMethod(method);
        }

        [Fact]
        public void is_containment_method_for_something_random()
        {
            isContainment<FakeThing>(x => x.IsOkay()).ShouldBeFalse();
        }

        [Fact]
        public void is_not_containment_method_for_string_contains()
        {
            isContainment<string>(x => x.Contains(null))
                .ShouldBeFalse();
        }

        [Fact]
        public void is_containment_for_enumerable_containes()
        {
            isContainment<IList<string>>(x => x.Contains("foo"))
                .ShouldBeTrue();
        }

        [Fact]
        public void is_containment_for_enumerable_any()
        {
            isContainment<IList<string>>(x => x.Any(_ => _.Contains("foo")))
                .ShouldBeTrue();
        }
    }
}