![Logo](favicon.ico)

# IndividualLock

sync / async lock for individual keys

Supports `.NET 4.5+`

## Getting Started

Install the [NuGet package](https://www.nuget.org/packages/IndividualLock/).

## Release Notes

See the [Release Notes](ReleaseNotes.md)

## Examples

Individual key has its own locking object, you can avoid to lock all the requests via one object.

And it can detect the locking object is being locked or not and release the idle locking objects according the expiration automatically.

FOR Sync scenario: use 'lock' to lock object

```C#
static readonly IndividualLocks mutex = new IndividualLocks(TimeSpan.FromHours(1));

public void Work(DateTime date, string people)
{
    var key = GenerateKey(date, people);

    lock (mutex.Lock(key))
    {
        // Do tasks
    }
}
```


FOR Async & Sync mixed scenario:  use 'using' to lock object

```C#
static readonly IndividualLocksAsync mutex = new IndividualLocksAsync(StringComparer.OrdinalIgnoreCase);

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







