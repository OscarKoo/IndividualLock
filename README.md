![Logo](favicon.ico)

# IndividualLock

sync / async lock for individual keys

Supports `.NET 4.5+`

## Getting Started

IndividualLocks Install the 3.0 [NuGet package](https://www.nuget.org/packages/Dao.IndividualLock).

IndividualReadWriteLocks Install the 1.0 [NuGet package](https://www.nuget.org/packages/Dao.IndividualReadWriteLock/1.0.0). 

## Release Notes

See the [Release Notes](ReleaseNotes.md)

## Examples

Individual key has its own locking object, you can avoid to lock all the requests via one object.

And it can release the locking objects (and keys) automatically.

FOR Sync scenario: use 'using' to lock object

```C#
static readonly IndividualLocks<string> mutex = new IndividualLocks<string>();

public void Work(DateTime date, string people)
{
    var key = GenerateKey(date, people);

    using (mutex.Lock(key))
    {
        // Do tasks
    }
}
```


FOR Async & Sync mixed scenario:  use 'using' to lock object

```C#
static readonly IndividualLocks<string> mutex = new IndividualLocks<string>(StringComparer.OrdinalIgnoreCase);

public void Work(string city, string area)
{
    var key = GenerateKey(city, area);

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    using (mutex.Lock(key, cts))
    {
        // Do tasks
    }
}

public async Task WorkAsync(string city, string area)
{
    var key = GenerateKey(city, area);

    using (await mutex.LockAsync(key))
    {
        // Do tasks
    }
}
```

Multiple Keys Lock, only one of the Works (Work1 & Work2) will get the lock

```C#
static readonly IndividualLocks<string> mutex = new IndividualLocks<string>();

public void Work1()
{
    var keys = new [] {"Key1", "Key2"};

    using (mutex.Lock(keys))
    {
        // Do tasks
    }
}

public void Work2()
{
    var keys = new [] {"Key2", "Key3"};

    using (mutex.Lock(keys))
    {
        // Do tasks
    }
}
```

IndividualReadWriteLocks Examples

```C#
static readonly IndividualReadWriteLocks<string> locks = new IndividualReadWriteLocks<string>(StringComparer.OrdinalIgnoreCase);

public void Read(string city, string area)
{
    var key = GenerateKey(city, area);

    using (mutex.ReaderLock(key))
    {
        // Do tasks
    }
}

public void Write(string city, string area)
{
    var key = GenerateKey(city, area);

    using (mutex.WriterLock(key))
    {
        // Do tasks
    }
}

public async Task WorkAsync(string city, string area)
{
    var key = GenerateKey(city, area);

    using (var upgrade = await mutex.UpgradeableReaderLockAsync(key))
    {
        // Do read tasks

        using (await upgrade.UpgradeAsync())
        {
            // Do write tasks
        }
    }
}
```






