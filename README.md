![Logo](favicon.ico)

# IndividualLock

sync / async lock for individual keys

Supports `.NET 4.5+`

## Getting Started

Install the 3.0 [NuGet package](https://www.nuget.org/packages/Dao.IndividualLock).

~~Install the 1.0 [NuGet package](https://www.nuget.org/packages/IndividualLock/).~~

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







