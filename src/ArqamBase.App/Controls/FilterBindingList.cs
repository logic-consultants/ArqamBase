using System.ComponentModel;

namespace ArqamBase.App.Controls;

/// <summary>Non-generic view of a list the grid can sort and filter like Access.</summary>
public interface IFilterableList : IBindingList
{
    int TotalCount { get; }
    /// <summary>Every item, including rows hidden by a filter.</summary>
    IEnumerable<object> AllObjects { get; }
    bool HasFilter(string property);
    bool AnyFilter { get; }
    IReadOnlyList<string> DistinctValues(string property);
    void SetFilter(string property, HashSet<string>? allowedValues, string? contains);
    void ClearFilter(string property);
    void ClearAllFilters();
    void SetFormatter(string property, Func<object?, string> formatter);
    void Sort(string property, ListSortDirection direction);
}

/// <summary>
/// A BindingList that keeps every item and shows only those matching the column filters, in the chosen sort order.
/// Rows added or removed through the grid are reported through ItemAdded and ItemRemoved.
/// </summary>
public sealed class FilterBindingList<T> : BindingList<T>, IFilterableList where T : class
{
    private readonly List<T> _all;
    private readonly Dictionary<string, (HashSet<string>? Allowed, string? Contains)> _filters = [];
    private readonly Dictionary<string, Func<object?, string>> _formatters = [];
    private readonly PropertyDescriptorCollection _props = TypeDescriptor.GetProperties(typeof(T));
    private PropertyDescriptor? _sortProp;
    private ListSortDirection _sortDir;
    private bool _rebuilding;

    public event Action<T>? ItemAdded;
    public event Action<T>? ItemRemoved;

    public FilterBindingList(IEnumerable<T> items)
    {
        _all = items.ToList();
        AllowNew = true;
        AllowRemove = true;
        Rebuild();
    }

    public IReadOnlyList<T> AllItems => _all;
    public int TotalCount => _all.Count;
    public IEnumerable<object> AllObjects => _all;
    public bool AnyFilter => _filters.Count > 0;
    public bool HasFilter(string property) => _filters.ContainsKey(property);

    public void SetFormatter(string property, Func<object?, string> formatter) => _formatters[property] = formatter;

    private Func<T, bool>? _search;

    /// <summary>An extra condition from the screen's search box and drop-downs; null shows everything.</summary>
    public void SetSearch(Func<T, bool>? predicate)
    {
        _search = predicate;
        Rebuild();
    }

    /// <summary>Adds a row that was created outside the grid (for example by a New button).</summary>
    public void AddExternal(T item)
    {
        _all.Add(item);
        _rebuilding = true;
        try { Add(item); } finally { _rebuilding = false; }
    }

    /// <summary>Removes a row without raising ItemRemoved.</summary>
    public void RemoveExternal(T item)
    {
        _all.Remove(item);
        _rebuilding = true;
        try { Remove(item); } finally { _rebuilding = false; }
    }

    private string Format(T item, string property)
    {
        var value = _props[property]?.GetValue(item);
        if (_formatters.TryGetValue(property, out var f))
            return f(value);
        return value switch
        {
            null => "",
            bool b => b ? "Yes" : "No",
            DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd/MM/yyyy") : d.ToString("dd/MM/yyyy HH:mm"),
            _ => value.ToString() ?? ""
        };
    }

    public IReadOnlyList<string> DistinctValues(string property) =>
        _all.Select(i => Format(i, property)).Distinct().OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();

    public void SetFilter(string property, HashSet<string>? allowedValues, string? contains)
    {
        if (allowedValues is null && string.IsNullOrWhiteSpace(contains))
            _filters.Remove(property);
        else
            _filters[property] = (allowedValues, string.IsNullOrWhiteSpace(contains) ? null : contains.Trim());
        Rebuild();
    }

    public void ClearFilter(string property)
    {
        if (_filters.Remove(property))
            Rebuild();
    }

    public void ClearAllFilters()
    {
        _filters.Clear();
        Rebuild();
    }

    public void Sort(string property, ListSortDirection direction)
    {
        var prop = _props[property];
        if (prop is not null)
            ApplySortCore(prop, direction);
    }

    private bool Matches(T item)
    {
        if (_search is not null && !_search(item))
            return false;
        foreach (var (property, (allowed, contains)) in _filters)
        {
            var text = Format(item, property);
            if (allowed is not null && !allowed.Contains(text))
                return false;
            if (contains is not null && !text.Contains(contains, StringComparison.CurrentCultureIgnoreCase))
                return false;
        }
        return true;
    }

    private void Rebuild()
    {
        IEnumerable<T> rows = _all.Where(Matches);
        if (_sortProp is not null)
        {
            var p = _sortProp;
            rows = _sortDir == ListSortDirection.Ascending
                ? rows.OrderBy(r => p.GetValue(r), Comparer<object?>.Create(CompareValues))
                : rows.OrderByDescending(r => p.GetValue(r), Comparer<object?>.Create(CompareValues));
        }

        _rebuilding = true;
        RaiseListChangedEvents = false;
        try
        {
            ClearItems();
            foreach (var r in rows.ToList())
                Add(r);
        }
        finally
        {
            RaiseListChangedEvents = true;
            _rebuilding = false;
        }
        ResetBindings();
    }

    private static int CompareValues(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (string x, string y) => string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase),
        (IComparable x, _) => x.CompareTo(b),
        _ => string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCulture)
    };

    protected override void InsertItem(int index, T item)
    {
        base.InsertItem(index, item);
        if (!_rebuilding)
        {
            _all.Add(item);
            ItemAdded?.Invoke(item);
        }
    }

    protected override void RemoveItem(int index)
    {
        var item = this[index];
        base.RemoveItem(index);
        if (!_rebuilding)
        {
            _all.Remove(item);
            ItemRemoved?.Invoke(item);
        }
    }

    // Sorting support used by DataGridView column headers.
    protected override bool SupportsSortingCore => true;
    protected override bool IsSortedCore => _sortProp is not null;
    protected override PropertyDescriptor? SortPropertyCore => _sortProp;
    protected override ListSortDirection SortDirectionCore => _sortDir;

    protected override void ApplySortCore(PropertyDescriptor prop, ListSortDirection direction)
    {
        _sortProp = prop;
        _sortDir = direction;
        Rebuild();
    }

    protected override void RemoveSortCore()
    {
        _sortProp = null;
        Rebuild();
    }
}
