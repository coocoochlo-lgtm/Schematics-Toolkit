using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Framework
{
    [Serializable]
    public class SerializableDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        private Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();

        // Set when Unity has just loaded `items`: the dictionary is built from them the first time it's used, not inside
        // OnAfterDeserialize, where Unity objects used as keys may not be loaded yet (comparing them to null there can
        // say "null", which used to drop them and leave the dictionary empty).
        [NonSerialized]
        private bool _pending;

        // The dictionary, built from the stored items first if they were just loaded.
        private Dictionary<TKey, TValue> dictionary
        {
            get
            {
                EnsureBuilt();
                return _dictionary;
            }
        }

        [SerializeField]
        private List<DictionaryItem> items = new List<DictionaryItem>();

        public TValue this[TKey key]
        {
            get
            {
                return dictionary.TryGetValue(key, out var value)
                    ? value
                    : default(TValue);
            }
            set
            {
                dictionary[key] = value;
            }
        }

        public ICollection<TKey> Keys
        {
            get { return dictionary.Keys; }
        }

        public ICollection<TValue> Values
        {
            get { return dictionary.Values; }
        }

        public void Add(TKey key, TValue value)
        {
            dictionary.TryAdd(key, value);
        }

        public bool ContainsKey(TKey key)
        {
            return dictionary.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            return dictionary.Remove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return dictionary.TryGetValue(key, out value);
        }

        public void Clear()
        {
            dictionary.Clear();
        }

        public int Count
        {
            get { return dictionary.Count; }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get { return (dictionary as ICollection<KeyValuePair<TKey, TValue>>).IsReadOnly; }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            (dictionary as ICollection<KeyValuePair<TKey, TValue>>).Add(item);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            return (dictionary as ICollection<KeyValuePair<TKey, TValue>>).Contains(item);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            (dictionary as ICollection<KeyValuePair<TKey, TValue>>).CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            return (dictionary as ICollection<KeyValuePair<TKey, TValue>>).Remove(item);
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return (dictionary as IEnumerable<KeyValuePair<TKey, TValue>>).GetEnumerator();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
            => dictionary.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public void OnBeforeSerialize()
        {
            // Not built since it was loaded: the stored items are still exactly its contents.
            if (_pending)
                return;

            items.Clear();
            foreach (var pair in _dictionary)
                items.Add(new DictionaryItem(pair.Key, pair.Value));
        }

        public void OnAfterDeserialize()
        {
            // Built on first use (see _pending); the items are kept, so nothing is lost if it's saved before then.
            _pending = true;
        }

        private void EnsureBuilt()
        {
            if (!_pending)
                return;
            _pending = false;

            _dictionary ??= new Dictionary<TKey, TValue>();
            _dictionary.Clear();
            if (items == null)
                return;

            foreach (var item in items)
            {
                // Plain reference check: an entry whose object is missing is kept rather than silently dropped.
                if (item == null || ReferenceEquals(item.key, null))
                    continue;
                if (!_dictionary.ContainsKey(item.key))
                    _dictionary.Add(item.key, item.value);
            }
        }

        public SerializableDictionary()
        {
        }

        /// <summary>
        /// Clones the other Dictionary into this one.
        /// </summary>
        /// <param name="from">From.</param>
        public SerializableDictionary(SerializableDictionary<TKey, TValue> from)
        {
            foreach (TKey key in from.Keys)
            {
                Add(key, from[key]);
            }
        }

        public IReadOnlyDictionary<TKey, TValue> GetReadOnlyDictionary()
        {
            return dictionary;
        }

        public KeyValuePair<TKey, TValue> KVPAt(int index)
        {
            if (index < 0 || index >= dictionary.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            int i = 0;
            foreach (var kvp in dictionary)
            {
                if (i++ == index)
                    return kvp;
            }

            throw new InvalidOperationException();
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            bool first = true;

            foreach (var pair in dictionary)
            {
                if (!first)
                    sb.Append(',');

                first = false;

                sb.Append(FormatValue(pair.Key));
                sb.Append(':');
                sb.Append(FormatValue(pair.Value));
            }

            return sb.ToString();
        }

        private static string FormatValue<T>(T value)
        {
            if (value is float f)
                return (Math.Truncate(f * 100) / 100).ToString();

            return value?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Creates a new serializable dictionary from a string representation.
        /// </summary>
        /// <param name="dictionaryString">The string representation of the dictionary.</param>
        /// <returns>A new serializable dictionary with the same key-value pairs as the string.</returns>
        public static SerializableDictionary<TKey, TValue> NewFromString(string dictionaryString)
        {
            var dictionary = new SerializableDictionary<TKey, TValue>();

            var items = dictionaryString.Split(',');

            foreach (var item in items)
            {
                var parts = item.Split(':');
                var keyString = parts[0];
                var valueString = parts[1];

                var key = (TKey)Convert.ChangeType(keyString, typeof(TKey));
                var value = (TValue)Convert.ChangeType(valueString, typeof(TValue));

                dictionary.Add(key, value);
            }

            // Return the dictionary
            return dictionary;
        }

        /// <summary>
        /// Sets the dictionary of this instance from a string representation.
        /// </summary>
        public void FromString(string dictionaryString)
        {
            _dictionary = NewFromString(dictionaryString).dictionary;
            _pending = false;
        }

        public static implicit operator Dictionary<TKey, TValue>(SerializableDictionary<TKey, TValue> serializableDictionary)
        {
            if (serializableDictionary == null)
                return null;

            return new Dictionary<TKey, TValue>(serializableDictionary.dictionary);
        }

        public static implicit operator SerializableDictionary<TKey, TValue>(Dictionary<TKey, TValue> normalDictionary)
        {
            if (normalDictionary == null)
                return null;

            var sDict = new SerializableDictionary<TKey, TValue>();
            foreach (var kvp in normalDictionary)
            {
                sDict.Add(kvp.Key, kvp.Value);
            }
            return sDict;
        }

        [Serializable]
        public class DictionaryItem
        {
            [SerializeField]
            public TKey key;
            [SerializeField]
            public TValue value;

            public DictionaryItem(TKey key, TValue value)
            {
                this.key = key;
                this.value = value;
            }
        }
    }
}