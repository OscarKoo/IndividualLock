using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dao.ConcurrentDictionaryLazy;

namespace Dao.IndividualLock
{
    [Serializable]
    public class IndividualLocks<TKey>
    {
        sealed class LockingObject : IDisposable
        {
            internal readonly SemaphoreSlim locker = new SemaphoreSlim(1, 1);

            internal TKey key;
            internal volatile IndividualLocks<TKey> host;
            internal volatile int count;

            volatile bool disposed;
            internal bool Disposed => this.disposed;

            internal void Dispose(bool release)
            {
                lock (this)
                {
                    this.count--;
                    if (release)
                    {
                        this.locker.Release();
                        Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({this.key}) released. (locking count: {this.count}, keys count: {this.host.objects.Count})");
                    }

                    if (this.count <= 0)
                    {
                        var tmpHost = this.host;
                        this.host = null;
                        tmpHost.objects.Remove(this.key);
                        this.locker.Dispose();
                        this.disposed = true;
                        Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({this.key}) removed! (locking count: {this.count}, keys count: {tmpHost.objects.Count})");
                    }
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }
        }

        readonly ConcurrentDictionaryLazy<TKey, LockingObject> objects;

        public IndividualLocks(IEqualityComparer<TKey> comparer = null)
        {
            this.objects = new ConcurrentDictionaryLazy<TKey, LockingObject>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int Count => this.objects.Count;

        LockingObject GetLocker(TKey key)
        {
            NewEntry:
            var locker = this.objects.GetOrAdd(key, k => new LockingObject());
            lock (locker)
            {
                if (locker.Disposed)
                    goto NewEntry;

                locker.key = key;
                locker.host = this;
                locker.count++;
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) acquiring the lock... (locking count: {locker.count}, keys count: {this.objects.Count})");
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
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) cancelled. (locking count: {locker.count}, keys count: {this.objects.Count})");
                locker.Dispose(false);
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the lock! (locking count: {locker.count}, keys count: {this.objects.Count})");
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
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) cancelled async. (locking count: {locker.count}, keys count: {this.objects.Count})");
                locker.Dispose(false);
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the lock async! (locking count: {locker.count}, keys count: {this.objects.Count})");
            return locker;
        }
    }
}