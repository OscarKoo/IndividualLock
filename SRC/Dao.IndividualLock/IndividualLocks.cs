using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

        #endregion

        readonly ConcurrentDictionary<TKey, Lazy<LockingObject>> objects;

        public IndividualLocks(IEqualityComparer<TKey> comparer = null)
        {
            this.objects = new ConcurrentDictionary<TKey, Lazy<LockingObject>>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int Count => this.objects.Count;

        public int Usage(TKey key)
        {
            return this.objects.TryGetValue(key, out var value) ? value.Value.Usage : 0;
        }

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
    }
}