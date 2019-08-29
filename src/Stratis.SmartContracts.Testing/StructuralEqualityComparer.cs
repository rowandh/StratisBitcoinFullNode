using System.Collections;
using System.Collections.Generic;

namespace Stratis.SmartContracts.Testing
{
    public class StructuralEqualityComparer<T> : IEqualityComparer<T>
    {
        private static readonly StructuralEqualityComparer<T> instance = new StructuralEqualityComparer<T>();

        static StructuralEqualityComparer()
        {
        }

        private StructuralEqualityComparer()
        {
        }

        public static StructuralEqualityComparer<T> Default => instance;

        public bool Equals(T x, T y)
        {
            return StructuralComparisons.StructuralEqualityComparer.Equals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
        }
    }
}