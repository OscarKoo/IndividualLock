using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nito.AsyncEx;

namespace IndividualLock
{
    public abstract class IndividualLock<TKey, TValue>
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
        DateTime nextCheckTime;

        protected IndividualLock(IEqualityComparer<TKey> comparer = null, TimeSpan? expiration = null)
        {
            this.expiration = expiration;
            this.nextCheckTime = expiration == null ? DateTime.MaxValue : GetNextCheckTime(DateTime.Now);
            this.objects = new ConcurrentDictionaryLazy<TKey, LockingObject>(comparer ?? EqualityComparer<TKey>.Default);
        }

        protected TValue GetLock(TKey key)
        {
            var now = DateTime.Now;

            RemoveExpiries(now);

            var expiry = this.expiration == null ? (DateTime?)null : GetNextCheckTime(now);
            return this.objects.AddOrUpdate(key, k => new LockingObject(expiry), (k, v) => v.Expiry = expiry).Data;
        }

        DateTime GetNextCheckTime(DateTime now)
        {
            return now.AddMilliseconds(this.expiration.Value.TotalMilliseconds);
        }

        bool RequireRemoveExpiries(DateTime now)
        {
            return this.expiration != null && this.nextCheckTime <= now;
        }

        void RemoveExpiries(DateTime now)
        {
            if (!RequireRemoveExpiries(now))
                return;

            lock (this.objects)
            {
                if (!RequireRemoveExpiries(now))
                    return;

                var nextTime = GetNextCheckTime(now);

                var expiries = this.objects.Where(w =>
                {
                    var expiry = w.Value.Expiry.Value;

                    if (expiry <= now)
                        return true;

                    if (expiry < nextTime)
                        nextTime = expiry;

                    return false;
                }).ToList();

                expiries.ParallelForEach(kv =>
                {
                    var obj = kv.Value.Data;
                    var asyncLock = obj as AsyncLock;

                    if (asyncLock == null && !obj.IsLocked()
                        || asyncLock != null && !asyncLock.IsLocked())
                        this.objects.Remove(kv.Key);
                });

                this.nextCheckTime = nextTime;
            }
        }
    }

    #region string / object

    public class IndividualLock<TKey> : IndividualLock<TKey, object>
    {
        public IndividualLock(IEqualityComparer<TKey> comparer) : this(comparer, null) { }

        public IndividualLock(TimeSpan? expiration) : this(null, expiration) { }

        public IndividualLock(IEqualityComparer<TKey> comparer = null, TimeSpan? expiration = null) : base(comparer, expiration) { }

        public object Lock(TKey key)
        {
            return GetLock(key);
        }
    }

    public class IndividualLock : IndividualLock<string>
    {
        public IndividualLock(IEqualityComparer<string> comparer) : this(comparer, null) { }

        public IndividualLock(TimeSpan? expiration) : this(null, expiration) { }

        public IndividualLock(IEqualityComparer<string> comparer = null, TimeSpan? expiration = null) : base(comparer ?? StringComparer.Ordinal, expiration) { }
    }

    #endregion

    #region string / AsyncLock

    public class IndividualLockAsync<TKey> : IndividualLock<TKey, AsyncLock>
    {
        public IndividualLockAsync(IEqualityComparer<TKey> comparer) : this(comparer, null) { }

        public IndividualLockAsync(TimeSpan? expiration) : this(null, expiration) { }

        public IndividualLockAsync(IEqualityComparer<TKey> comparer = null, TimeSpan? expiration = null) : base(comparer, expiration) { }

        public IDisposable Lock(TKey key, CancellationToken? cancellationToken = null)
        {
            return GetLock(key).Lock(cancellationToken ?? CancellationToken.None);
        }

        public AwaitableDisposable<IDisposable> LockAsync(TKey key, CancellationToken? cancellationToken = null)
        {
            return GetLock(key).LockAsync(cancellationToken ?? CancellationToken.None);
        }
    }

    public class IndividualLockAsync : IndividualLockAsync<string>
    {
        public IndividualLockAsync(IEqualityComparer<string> comparer) : this(comparer, null) { }

        public IndividualLockAsync(TimeSpan? expiration) : this(null, expiration) { }

        public IndividualLockAsync(IEqualityComparer<string> comparer = null, TimeSpan? expiration = null) : base(comparer ?? StringComparer.Ordinal, expiration) { }
    }

    #endregion
}