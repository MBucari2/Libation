﻿using Avalonia.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace LibationAvalonia.ViewModels
{
	/// <summary>
	/// This compare class ensures that all top-level grid entries (standalone books or series parents)
	/// are sorted by PropertyName while all episodes remain immediately beneath their parents and remain
	/// sorted by series index, ascending. Stable sorting is achieved by comparing the GridEntry.ListIndex
	/// properties when 2 items compare equal.
	/// </summary>
	internal class RowComparer : IComparer, IComparer<GridEntry>, IComparer<object>
	{
		private static readonly PropertyInfo HeaderCellPi = typeof(DataGridColumn).GetProperty("HeaderCell", BindingFlags.NonPublic | BindingFlags.Instance);
		private static readonly PropertyInfo CurrentSortingStatePi = typeof(DataGridColumnHeader).GetProperty("CurrentSortingState", BindingFlags.NonPublic | BindingFlags.Instance);

		public DataGridColumn Column { get; init; }
		public string PropertyName { get; private set; }

		public RowComparer(DataGridColumn column)
		{
			Column = column;
			PropertyName = Column.SortMemberPath;
		}

		public int Compare(object x, object y)
		{
			if (x is null && y is not null) return -1;
			if (x is not null && y is null) return 1;
			if (x is null && y is null) return 0;

			var geA = (GridEntry)x;
			var geB = (GridEntry)y;

			var sortDirection = GetSortOrder();

			SeriesEntry parentA = null;
			SeriesEntry parentB = null;

			if (geA is LibraryBookEntry lbA && lbA.Parent is SeriesEntry seA)
				parentA = seA;
			if (geB is LibraryBookEntry lbB && lbB.Parent is SeriesEntry seB)
				parentB = seB;

			//both a and b are top-level grid entries
			if (parentA is null && parentB is null)
				return InternalCompare(geA, geB, sortDirection);

			//a is top-level, b is a child
			if (parentA is null && parentB is not null)
			{
				// b is a child of a, parent is always first
				if (parentB == geA)
					return sortDirection is ListSortDirection.Ascending ? -1 : 1;
				else
					return InternalCompare(geA, parentB, sortDirection);
			}

			//a is a child, b is a top-level
			if (parentA is not null && parentB is null)
			{
				// a is a child of b, parent is always first
				if (parentA == geB)
					return sortDirection is ListSortDirection.Ascending ? 1 : -1;
				else
					return InternalCompare(parentA, geB, sortDirection);
			}

			//both are children of the same series, always present in order of series index, ascending
			if (parentA == parentB)
				return geA.SeriesIndex.CompareTo(geB.SeriesIndex) * (sortDirection is ListSortDirection.Ascending ? 1 : -1);

			//a and b are children of different series.
			return InternalCompare(parentA, parentB, sortDirection);
		}

		//Avalonia doesn't expose the column's CurrentSortingState, so we must get it through reflection
		private ListSortDirection? GetSortOrder()
			=> CurrentSortingStatePi.GetValue(HeaderCellPi.GetValue(Column)) as ListSortDirection?;

		private int InternalCompare(GridEntry x, GridEntry y, ListSortDirection? sortDirection)
		{
			var val1 = x.GetMemberValue(PropertyName);
			var val2 = y.GetMemberValue(PropertyName);

			var compareResult = x.GetMemberComparer(val1.GetType()).Compare(val1, val2);

			//If items compare equal, compare them by their positions in the the list.
			//This is how you achieve a stable sort.
			if (compareResult == 0)
				return x.ListIndex.CompareTo(y.ListIndex) * (sortDirection is ListSortDirection.Ascending ? 1 : -1);
			else
				return compareResult;
		}

		public int Compare(GridEntry x, GridEntry y)
		{
			return Compare((object)x, y);
		}
	}
}
