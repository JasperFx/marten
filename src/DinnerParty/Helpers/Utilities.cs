using System;
using System.Collections.Generic;

namespace DinnerParty.Helpers
{
    public static class Utilities
    {
        public static void Each<T>(this IEnumerable<T> col, Action<T> action)
        {
            foreach(var i in col)
            {
                action(i);
            }
        }
    }
}