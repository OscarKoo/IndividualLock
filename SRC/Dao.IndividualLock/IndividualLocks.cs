using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dao.IndividualLock
{
    [Serializable]
    public class IndividualLocks<TKey>
    {
        #region LockingObject

        sealed class LockingObject : IDisposable
        {
            readonly object syncObj = new object();
            internal readonly SemaphoreSlim locker = new SemaphoreSlim(1, 1);

            TKey key;
            volatile IndividualLocks<TKey> host;

            internal LockingObject(IndividualLocks<TKey> host, TKey key)
            {
                this.host = host;
                this.key = key;
            }

            volatile int usage;
            internal int Usage => this.usage;

            volatile bool disposed;

            internal LockingObject Capture()
            {
                lock (this.syncObj)
                {
                    if (this.disposed)
                        return null;

                    this.usage++;
                    return this;
                }
            }

            internal void Release(bool release)
            {
                lock (this.syncObj)
                {
                    this.usage--;
                    if (release)
                    {
                        this.locker.Release();
                        Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({this.key}) released. (locking usage: {Usage}, keys count: {this.host.Count})");
                    }

                    if (this.usage <= 0)
                    {
                        var tmpHost = this.host;
                        this.host = null;
                        var tmpKey = this.key;
                        this.key = default;
                        tmpHost.objects.TryRemove(tmpKey, out _);
                        this.locker.Dispose();
                        this.disposed = true;
                        Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({this.key}) removed! (locking usage: {Usage}, keys count: {tmpHost.Count})");
                    }
                }
            }

            public void Dispose()
            {
                Release(true);
            }
        }

        sealed class MultipleLockingObject : IDisposable
        {
            IReadOnlyList<IDisposable> lockingObjects;

            internal MultipleLockingObject(IReadOnlyList<IDisposable> lockingObjects) => this.lockingObjects = lockingObjects;

            public void Dispose()
            {
                if (this.lockingObjects == null || this.lockingObjects.Count <= 0)
                    return;

                for (var i = this.lockingObjects.Count - 1; i >= 0; i--)
                {
                    this.lockingObjects[i].Dispose();
                }

                this.lockingObjects = null;
            }
        }

        #endregion

        readonly IEqualityComparer<TKey> keyComparer;
        readonly ConcurrentDictionary<TKey, Lazy<LockingObject>> objects;

        public IndividualLocks(IEqualityComparer<TKey> comparer = null)
        {
            this.keyComparer = comparer ?? EqualityComparer<TKey>.Default;
            this.objects = new ConcurrentDictionary<TKey, Lazy<LockingObject>>(this.keyComparer);
        }

        public int Count => this.objects.Count;

        public int Usage(TKey key) => this.objects.TryGetValue(key, out var value) ? value.Value.Usage : 0;

        public int Usage(IEnumerable<TKey> keys) => keys.Select(key => this.objects.TryGetValue(key, out var value) ? value.Value.Usage : 0).Max();

        LockingObject GetLocker(TKey key)
        {
            LockingObject locker;
            do
            {
                locker = this.objects.GetOrAdd(key, k => new Lazy<LockingObject>(() => new LockingObject(this, k))).Value.Capture();
            } while (locker == null);

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) acquiring the lock... (locking usage: {locker.Usage}, keys count: {Count})");
            return locker;
        }

        static void DisposeMultiple(IReadOnlyList<IDisposable> lockingObjects)
        {
            if (lockingObjects.Count <= 0)
                return;

            for (var i = lockingObjects.Count - 1; i >= 0; i--)
            {
                lockingObjects[i].Dispose();
            }
        }

        public IDisposable Lock(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            try
            {
                locker.locker.Wait(cancellationToken);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) cancelled. (locking usage: {locker.Usage}, keys count: {Count})");
                locker.Release(false);
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the lock! (locking usage: {locker.Usage}, keys count: {Count})");
            return locker;
        }

        public IDisposable Lock(IEnumerable<TKey> keys, IComparer<TKey> comparer = null, CancellationToken cancellationToken = new CancellationToken())
        {
            var lockingObjects = new List<IDisposable>();
            foreach (var key in keys.Distinct(this.keyComparer).OrderBy(o => o, comparer ?? Comparer<TKey>.Default))
            {
                try
                {
                    lockingObjects.Add(Lock(key, cancellationToken));
                }
                catch (Exception ex)
                {
                    DisposeMultiple(lockingObjects);
                    throw;
                }
            }

            return new MultipleLockingObject(lockingObjects.AsReadOnly());
        }

        public async Task<IDisposable> LockAsync(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            try
            {
                await locker.locker.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) cancelled async. (locking usage: {locker.Usage}, keys count: {Count})");
                locker.Release(false);
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the lock async! (locking usage: {locker.Usage}, keys count: {Count})");
            return locker;
        }

        public async Task<IDisposable> LockAsync(IEnumerable<TKey> keys, IComparer<TKey> comparer = null, CancellationToken cancellationToken = new CancellationToken())
        {
            var lockingObjects = new List<IDisposable>();
            foreach (var key in keys.Distinct(this.keyComparer).OrderBy(o => o, comparer ?? Comparer<TKey>.Default))
            {
                try
                {
                    lockingObjects.Add(await LockAsync(key, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    DisposeMultiple(lockingObjects);
                    throw;
                }
            }

            return new MultipleLockingObject(lockingObjects.AsReadOnly());
        }
    }
}