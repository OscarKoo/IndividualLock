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
            internal readonly object syncObj = new object();
            internal readonly SemaphoreSlim locker = new SemaphoreSlim(1, 1);

            TKey key;
            volatile IndividualLocks<TKey> host;

            internal LockingObject(IndividualLocks<TKey> host, TKey key)
            {
                this.host = host;
                this.key = key;
            }

            volatile int usage;
            internal int Usage => Interlocked.CompareExchange(ref this.usage, 0, 0);

            volatile bool disposed;
            internal bool Disposed => this.disposed;

            internal void Capture()
            {
                lock (this.syncObj)
                {
                    if (this.disposed)
                        throw new ObjectDisposedException(nameof(LockingObject));

                    Interlocked.Increment(ref this.usage);
                }
            }

            internal void Release(bool release)
            {
                lock (this.syncObj)
                {
                    Interlocked.Decrement(ref this.usage);
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
                        this.key = default(TKey);
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

        readonly ConcurrentDictionary<TKey, LockingObject> objects;

        public IndividualLocks(IEqualityComparer<TKey> comparer = null)
        {
            this.objects = new ConcurrentDictionary<TKey, LockingObject>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int Count => this.objects.Count;

        public int Usage(TKey key)
        {
            return this.objects.TryGetValue(key, out var value) ? value.Usage : 0;
        }

        LockingObject GetLocker(TKey key)
        {
            NewEntry:
            var locker = this.objects.GetOrAdd(key, k => new LockingObject(this, k));
            lock (locker.syncObj)
            {
                if (locker.Disposed)
                    goto NewEntry;

                locker.Capture();
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) acquiring the lock... (locking usage: {locker.Usage}, keys count: {Count})");
                return locker;
            }
        }

        public IDisposable Lock(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            try
            {
                locker.locker.Wait(cancellationToken);
            }
            catch (Exception ex)
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
            catch (Exception ex)
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