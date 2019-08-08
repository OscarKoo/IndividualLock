using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dao.ConcurrentDictionaryLazy;
using Nito.AsyncEx;

namespace Dao.IndividualLock
{
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

            var now = DateTime.Now;
            RemoveExpiries(now);

            expiry = GetNextCheckTime(now);

            if (this.nextCheckTime == null)
                this.nextCheckTime = expiry;

            return this.objects.AddOrUpdate(key, k => new LockingObject(expiry), (k, v) => v.Expiry = expiry).Data;
        }

        DateTime GetNextCheckTime(DateTime now)
        {
            return now.AddMilliseconds(this.expiration.Value.TotalMilliseconds);
        }

        bool RequireRemoveExpiries(DateTime now)
        {
            return this.expiration != null && this.nextCheckTime != null && this.nextCheckTime <= now;
        }

        void RemoveExpiries(DateTime now)
        {
            if (!RequireRemoveExpiries(now))
                return;

            lock (this.objects)
            {
                if (!RequireRemoveExpiries(now))
                    return;

                var expiries = this.objects.Where(w => w.Value.Expiry <= now).ToList();

                expiries.ParallelForEach(kv =>
                {
                    var obj = kv.Value.Data;
                    var asyncLock = obj as AsyncLock;

                    if (asyncLock == null && !obj.IsLocked()
                        || asyncLock != null && !asyncLock.IsLocked())
                        this.objects.Remove(kv.Key);
                });

                this.nextCheckTime = GetNextCheckTime(now);
            }
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