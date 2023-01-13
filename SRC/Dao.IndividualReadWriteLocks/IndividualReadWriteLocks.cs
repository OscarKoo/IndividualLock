using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dao.ConcurrentDictionaryLazy;
using Nito.AsyncEx;

namespace Dao.IndividualReadWriteLocks
{
    public class IndividualReadWriteLocks<TKey>
    {
        #region LockingObject

        sealed class LockingObject : IDisposable
        {
            internal readonly object syncObj = new object();
            internal readonly AsyncReaderWriterLock locker = new AsyncReaderWriterLock();

            TKey key;
            volatile IndividualReadWriteLocks<TKey> host;

            volatile int refCount;
            internal int RefCount => this.refCount;

            volatile bool disposed;
            internal bool Disposed => this.disposed;

            internal void Bind(TKey key, IndividualReadWriteLocks<TKey> host)
            {
                lock (this.syncObj)
                {
                    if (this.disposed)
                        throw new ObjectDisposedException(nameof(LockingObject));

                    this.key = key;
                    this.host = host;
                    this.refCount++;
                }
            }

            internal void Release(Action releaseAction = null)
            {
                lock (this.syncObj)
                {
                    this.refCount--;
                    if (releaseAction != null)
                    {
                        releaseAction();
                        Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({this.key}) released. (locking count: {RefCount}, keys count: {this.host.Count})");
                    }

                    if (this.refCount <= 0)
                    {
                        var tmpHost = this.host;
                        this.host = null;
                        tmpHost.objects.Remove(this.key);
                        this.disposed = true;
                        Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({this.key}) removed! (locking count: {RefCount}, keys count: {tmpHost.Count})");
                    }
                }
            }

            public void Dispose()
            {
                Release();
            }
        }

        class LockingReadWriteInstance : IDisposable
        {
            LockingObject obj;
            protected IDisposable disposable;

            protected internal LockingReadWriteInstance(LockingObject obj, IDisposable disposable)
            {
                this.obj = obj;
                this.disposable = disposable;
            }

            public void Dispose()
            {
                var tmpObj = this.obj;
                this.obj = null;
                tmpObj.Release(this.disposable.Dispose);
                this.disposable = null;
            }
        }

        sealed class LockingUpgradeableReaderInstance : LockingReadWriteInstance, IUpgradeableReader
        {
            internal LockingUpgradeableReaderInstance(LockingObject obj, IDisposable disposable) : base(obj, disposable) { }

            public IDisposable Upgrade(CancellationToken cancellationToken = new CancellationToken())
            {
                return ((AsyncReaderWriterLock.UpgradeableReaderKey)this.disposable).Upgrade(cancellationToken);
            }

            public async Task<IDisposable> UpgradeAsync(CancellationToken cancellationToken = new CancellationToken())
            {
                return await ((AsyncReaderWriterLock.UpgradeableReaderKey)this.disposable).UpgradeAsync(cancellationToken);
            }
        }

        public interface IUpgradeableReader : IDisposable
        {
            IDisposable Upgrade(CancellationToken cancellationToken = new CancellationToken());

            Task<IDisposable> UpgradeAsync(CancellationToken cancellationToken = new CancellationToken());
        }

        #endregion

        readonly ConcurrentDictionaryLazy<TKey, LockingObject> objects;

        public IndividualReadWriteLocks(IEqualityComparer<TKey> comparer = null)
        {
            this.objects = new ConcurrentDictionaryLazy<TKey, LockingObject>(comparer ?? EqualityComparer<TKey>.Default);
        }

        public int Count => this.objects.Count;

        LockingObject GetLocker(TKey key)
        {
            NewEntry:
            var locker = this.objects.GetOrAdd(key, k => new LockingObject());
            lock (locker.syncObj)
            {
                if (locker.Disposed)
                    goto NewEntry;

                locker.Bind(key, this);
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) acquiring the lock... (locking count: {locker.RefCount}, keys count: {Count})");
                return locker;
            }
        }

        #region ReaderLock

        public IDisposable ReaderLock(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            IDisposable disposable;
            try
            {
                disposable = locker.locker.ReaderLock(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) ReaderLock cancelled. (locking count: {locker.RefCount}, keys count: {Count})");
                locker.Release();
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the ReaderLock! (locking count: {locker.RefCount}, keys count: {Count})");
            return new LockingReadWriteInstance(locker, disposable);
        }

        public async Task<IDisposable> ReaderLockAsync(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            IDisposable disposable;
            try
            {
                disposable = await locker.locker.ReaderLockAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) ReaderLockAsync cancelled. (locking count: {locker.RefCount}, keys count: {Count})");
                locker.Release();
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the ReaderLockAsync! (locking count: {locker.RefCount}, keys count: {Count})");
            return new LockingReadWriteInstance(locker, disposable);
        }

        #endregion

        #region WriterLock

        public IDisposable WriterLock(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            IDisposable disposable;
            try
            {
                disposable = locker.locker.WriterLock(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) WriterLock cancelled. (locking count: {locker.RefCount}, keys count: {Count})");
                locker.Release();
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the WriterLock! (locking count: {locker.RefCount}, keys count: {Count})");
            return new LockingReadWriteInstance(locker, disposable);
        }

        public async Task<IDisposable> WriterLockAsync(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            IDisposable disposable;
            try
            {
                disposable = await locker.locker.WriterLockAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) WriterLockAsync cancelled. (locking count: {locker.RefCount}, keys count: {Count})");
                locker.Release();
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the WriterLockAsync! (locking count: {locker.RefCount}, keys count: {Count})");
            return new LockingReadWriteInstance(locker, disposable);
        }

        #endregion

        #region UpgradeableReaderLock

        public IUpgradeableReader UpgradeableReaderLock(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            IDisposable disposable;
            try
            {
                disposable = locker.locker.UpgradeableReaderLock(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) UpgradeableReaderLock cancelled. (locking count: {locker.RefCount}, keys count: {Count})");
                locker.Release();
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the UpgradeableReaderLock! (locking count: {locker.RefCount}, keys count: {Count})");
            return new LockingUpgradeableReaderInstance(locker, disposable);
        }

        public async Task<IUpgradeableReader> UpgradeableReaderLockAsync(TKey key, CancellationToken cancellationToken = new CancellationToken())
        {
            var locker = GetLocker(key);
            IDisposable disposable;
            try
            {
                disposable = await locker.locker.UpgradeableReaderLockAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) UpgradeableReaderLockAsync cancelled. (locking count: {locker.RefCount}, keys count: {Count})");
                locker.Release();
                throw;
            }

            Debug.WriteLine($"[{DateTime.Now:yyyy-MM-dd:HH:mm:ss.fff} ({Thread.CurrentThread.ManagedThreadId})] Key ({key}) got the UpgradeableReaderLockAsync! (locking count: {locker.RefCount}, keys count: {Count})");
            return new LockingUpgradeableReaderInstance(locker, disposable);
        }

        #endregion
    }
}