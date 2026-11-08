using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SolutionComponentComparer.UI
{
    /// <summary>BindingList that lets a DataGridView sort by clicking column headers.</summary>
    public class SortableBindingList<T> : BindingList<T>
    {
        private bool _isSorted;
        private ListSortDirection _direction;
        private PropertyDescriptor _property;

        public SortableBindingList()
        {
        }

        public SortableBindingList(IList<T> items) : base(items.ToList())
        {
        }

        protected override bool SupportsSortingCore { get { return true; } }
        protected override bool IsSortedCore { get { return _isSorted; } }
        protected override ListSortDirection SortDirectionCore { get { return _direction; } }
        protected override PropertyDescriptor SortPropertyCore { get { return _property; } }

        protected override void ApplySortCore(PropertyDescriptor prop, ListSortDirection direction)
        {
            var items = (List<T>)Items;
            Comparison<T> comparison = (a, b) => CompareValues(prop.GetValue(a), prop.GetValue(b));
            items.Sort(direction == ListSortDirection.Ascending ? comparison : (a, b) => comparison(b, a));
            _property = prop;
            _direction = direction;
            _isSorted = true;
            OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }

        protected override void RemoveSortCore()
        {
            _isSorted = false;
            _property = null;
        }

        private static int CompareValues(object a, object b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            var sa = a as string;
            if (sa != null) return string.Compare(sa, b as string, StringComparison.OrdinalIgnoreCase);
            var ca = a as IComparable;
            if (ca != null) return ca.CompareTo(b);
            return Comparer.Default.Compare(a.ToString(), b.ToString());
        }
    }
}
