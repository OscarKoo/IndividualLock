using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dao.ConcurrentDictionaryLazy;
using Nito.AsyncEx;

namespace Dao.IndividualLock
{
    [Serializable]
    public abstract class IndividualLocks<TKey, TValue>
        where TValue : new()
    {
        class LockingObject
        {
            internal LockingObject(DateTime? expiry = null)
            {
                Expiry = expiry;
            }

            internal readonly TValue Data = new TValue();
            internal DateTime? Expiry { get; set; }
        }

        readonly TimeSpan? expiration;
        readonly ConcurrentDictionaryLazy<TKey, LockingObject> objects;
        DateTime? nextCheckTime;
        readonly SynchronizedCollection<DateTime> timeStack = new SynchronizedCollection<DateTime>();

        protected IndividualLocks(IEqualityComparer<TKey> comparer = null, TimeSpan? expiration = null)
        {
            this.expiration = expiration;
            this.objects = new ConcurrentDictionaryLazy<TKey, LockingObject>(comparer ?? EqualityComparer<TKey>.Default);
        }

        protected TValue GetLock(TKey key)
        {
            DateTime? expiry = null;

            if (this.expiration == null)
                return this.objects.GetOrAdd(key, k => new LockingObject(expiry)).Data;

            var now = RemoveExpiries(key);

            expiry = NewExpiryTime(now);

            if (this.nextCheckTime == null)
                this.nextCheckTime = GetNextCheckTime(now);

            return this.objects.AddOrUpdate(key, k => new LockingObject(expiry), (k, v) => v.Expiry = expiry).Data;
        }

        DateTime NewExpiryTime(DateTime now)
        {
            return now.AddMilliseconds(this.expiration.Value.TotalMilliseconds);
        }

        DateTime GetNextCheckTime(DateTime checkTime)
        {
            return checkTime.AddMilliseconds(Math.Max(60000, this.expiration.Value.TotalMilliseconds / 10));
        }

        bool RequireRemoveExpiries(DateTime now)
        {
            return this.expiration != null && this.nextCheckTime != null && this.nextCheckTime <= now;
        }

        static bool IsExpired(LockingObject obj, DateTime checkTime)
        {
            return obj.Expiry <= checkTime;
        }

        // Pop the last stack item.
        void PopTimeStack()
        {
            lock (this.timeStack.SyncRoot)
            {
                this.timeStack.RemoveAt(this.timeStack.Count - 1);
            }
        }

        DateTime RemoveExpiries(TKey key)
        {
            var now = DateTime.Now;

            if (!RequireRemoveExpiries(now))
                return now;

            var isFirst = this.timeStack.Count == 0;

            // push now into the stack
            this.timeStack.Add(now);

            // get the first check time.
            var checkTime = this.timeStack[0];

            var isLocked = false;
            if (RequireRemoveExpiries(checkTime)
                && this.objects.TryGetValue(key, out var lockingObject)
                && IsExpired(lockingObject, checkTime))
            {
                if (!isFirst)
                {
                    if (!Monitor.TryEnter(this))
                    {
                        lock (lockingObject)
                        {
                            lockingObject.Expiry = NewExpiryTime(now);
                        }

                        PopTimeStack();
                        return now;
                    }

                    isLocked = true;
                }

                try
                {
                    lock (this)
                    {
                        if (RequireRemoveExpiries(checkTime))
                        {
                            var expiries = this.objects.Where(w => IsExpired(w.Value, checkTime)).ToList();

                            expiries.ParallelForEach(kv =>
                            {
                                var obj = kv.Value.Data;
                                var asyncLock = obj as AsyncLock;

                                lock (kv.Value)
                                {
                                    if (IsExpired(kv.Value, checkTime)
                                        && (asyncLock == null && !obj.IsLocked()
                                            || asyncLock != null && !asyncLock.IsLocked()))
                                        this.objects.Remove(kv.Key);
                                }
                            });

                            this.nextCheckTime = GetNextCheckTime(checkTime);
                        }
                    }
                }
                finally
                {
                    if (isLocked)
                        Monitor.Exit(this);
                }
            }

            PopTimeStack();
            return now;
        }
    }

    #region string / object

    public class IndividualLocks<TKey> : IndividualLocks<TKey, object>
    {
        public IndividualLocks(IEqualityComparer<TKey> comparer) : this(comparer, null) { }

        public IndividualLocks(TimeSpan? expiration) : this(null, expiration) { }

        public IndividualLocks(IEqualityComparer<TKey> comparer = null, TimeSpan? expiration = null) : base(comparer, expiration) { }

        public object Lock(TKey key)
        {
            return GetLock(key);
        }
    }

    public class IndividualLocks : IndividualLocks<string>
    {
        public IndividualLocks(IEqualityComparer<string> comparer) : this(comparer, null) { }

        public IndividualLocks(TimeSpan? expiration) : this(null, expiration) { }

        public IndividualLocks(IEqualityComparer<string> comparer = null, TimeSpan? expiration = null) : base(comparer ?? StringComparer.Ordinal, expiration) { }
    }

    #endregion

    #region string / AsyncLock

    public class IndividualLocksAsync<TKey> : IndividualLocks<TKey, AsyncLock>
    {
        public IndividualLocksAsync(IEqualityComparer<TKey> comparer) : this(comparer, null) { }

        public IndividualLocksAsync(TimeSpan? expiration) : this(null, expiration) { }

        public IndividualLocksAsync(IEqualityComparer<TKey> comparer = null, TimeSpan? expiration = null) : base(comparer, expiration) { }

        public IDisposable Lock(TKey key, CancellationToken? cancellationToken = null)
        {
            return GetLock(key).Lock(cancellationToken ?? CancellationToken.None);
        }

        public AwaitableDisposable<IDisposable> LockAsync(TKey key, CancellationToken? cancellationToken = null)
        {
            return GetLock(key).LockAsync(cancellationToken ?? CancellationToken.None);
        }
    }

    public class IndividualLocksAsync : IndividualLocksAsync<string>
    {
        public IndividualLocksAsync(IEqualityComparer<string> comparer) : this(comparer, null) { }

        public IndividualLocksAsync(TimeSpan? expiration) : this(null, expiration) { }

        public IndividualLocksAsync(IEqualityComparer<string> comparer = null, TimeSpan? expiration = null) : base(comparer ?? StringComparer.Ordinal, expiration) { }
    }

    #endregion
}