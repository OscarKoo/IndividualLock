using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IndividualLock
{
    class ConcurrentDictionaryLazy<TKey, TValue> : IDictionary<TKey, TValue>
    {
        readonly ConcurrentDictionary<TKey, Lazy<TValue>> dictionary;

        public ConcurrentDictionaryLazy(IEqualityComparer<TKey> comparer = null)
        {
            if (comparer == null)
                comparer = EqualityComparer<TKey>.Default;

            this.dictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>(comparer);
        }

        static Lazy<TValue> NewValue(TValue value)
        {
            return new Lazy<TValue>(() => value);
        }

        static Lazy<TValue> NewValue(TKey key, Func<TKey, TValue> addValueFactory)
        {
            return new Lazy<TValue>(() => addValueFactory(key));
        }

        static Lazy<TValue> NewValue(TKey key, Lazy<TValue> value, Func<TKey, TValue, TValue> updateValueFactory)
        {
            return new Lazy<TValue>(() => updateValueFactory(key, value.Value));
        }

        public bool TryAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            return this.dictionary.TryAdd(key, NewValue(key, valueFactory));
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (valueFactory == null)
                throw new ArgumentNullException(nameof(valueFactory));

            return this.dictionary.GetOrAdd(key, k => NewValue(k, valueFactory)).Value;
        }

        /// <summary>
        /// Update will update existing value
        /// </summary>
        public TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Action<TKey, TValue> updateValueFactory)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (addValueFactory == null)
                throw new ArgumentNullException(nameof(addValueFactory));

            if (updateValueFactory == null)
                throw new ArgumentNullException(nameof(updateValueFactory));

            return this.dictionary.AddOrUpdate(key, k => NewValue(k, addValueFactory), (k, v) =>
            {
                updateValueFactory(k, v.Value);
                return v;
            }).Value;
        }

        /// <summary>
        /// Update will replace existing value
        /// </summary>
        public TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (addValueFactory == null)
                throw new ArgumentNullException(nameof(addValueFactory));

            if (updateValueFactory == null)
                throw new ArgumentNullException(nameof(updateValueFactory));

            return this.dictionary.AddOrUpdate(key, k => NewValue(k, addValueFactory), (k, v) => NewValue(k, v, updateValueFactory)).Value;
        }

        #region IEnumerable

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            foreach (var kv in this.dictionary)
            {
                yield return new KeyValuePair<TKey, TValue>(kv.Key, kv.Value.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        static TValue CheckValue(Lazy<TValue> value)
        {
            return value == null ? default(TValue) : value.Value;
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            var result = this.dictionary.TryRemove(key, out var lazy);
            value = CheckValue(lazy);
            return result;
        }

        #region IDictionary

        public bool ContainsKey(TKey key)
        {
            return this.dictionary.ContainsKey(key);
        }

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
        {
            this.dictionary.TryAdd(key, NewValue(value));
        }

        public bool Remove(TKey key)
        {
            return this.dictionary.TryRemove(key, out _);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            ((IDictionary<TKey, TValue>)this).Add(item.Key, item.Value);
        }

        public void Clear()
        {
            this.dictionary.Clear();
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            return TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            var items = new KeyValuePair<TKey, Lazy<TValue>>[array.Length];
            ((ICollection<KeyValuePair<TKey, Lazy<TValue>>>)this.dictionary).CopyTo(items, arrayIndex);
            for (var i = arrayIndex; i < items.Length; i++)
            {
                var current = items[i];
                array[i] = new KeyValuePair<TKey, TValue>(current.Key, current.Value.Value);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            return Remove(item.Key);
        }

        public int Count => this.dictionary.Count;

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => ((ICollection<KeyValuePair<TKey, Lazy<TValue>>>)this.dictionary).IsReadOnly;

        public ICollection<TKey> Keys => this.dictionary.Keys;

        public ICollection<TValue> Values
        {
            get { return this.dictionary.Values.Select(s => s.Value).ToList().AsReadOnly(); }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            var result = this.dictionary.TryGetValue(key, out var lazy);
            value = CheckValue(lazy);
            return result;
        }

        public TValue this[TKey key]
        {
            get => CheckValue(this.dictionary[key]);
            set => this.dictionary[key] = NewValue(value);
        }

        #endregion
    }
}