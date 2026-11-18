using System.Collections;
using System.Collections.Immutable;

namespace Kairo.SourceGenerator;

/// <summary>
/// An immutable array compared by its items. An incremental generator skips its output step when
/// the model equals the previous one, and ImmutableArray alone is compared by reference.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items)
    {
        _items = items;
    }

    public int Count
    {
        get
        {
            if (_items.IsDefault)
            {
                return 0;
            }

            return _items.Length;
        }
    }

    public bool Equals(EquatableArray<T> other)
    {
        if (Count != other.Count)
        {
            return false;
        }

        for (int i = 0; i < Count; i++)
        {
            if (!_items[i].Equals(other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        if (obj is EquatableArray<T> other)
        {
            return Equals(other);
        }

        return false;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (T item in this)
        {
            hash = (hash * 31) + item.GetHashCode();
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (_items.IsDefault)
        {
            return Enumerable.Empty<T>().GetEnumerator();
        }

        return ((IEnumerable<T>)_items).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
    {
        return !left.Equals(right);
    }
}
