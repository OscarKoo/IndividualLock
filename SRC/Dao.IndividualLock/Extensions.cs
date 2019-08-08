using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Dao.IndividualLock
{
    static class Extensions
    {
        static readonly FieldInfo asyncLock_Mutex = typeof(AsyncLock).GetField("_mutex", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo asyncLock_Taken = typeof(AsyncLock).GetField("_taken", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsLocked(this AsyncLock source)
        {
            if (source == null)
                return false;

            lock (asyncLock_Mutex.GetValue(source))
            {
                return (bool)asyncLock_Taken.GetValue(source);
            }
        }

        public static bool IsLocked(this object source)
        {
            if (source == null)
                return false;

            if (!Monitor.TryEnter(source))
                return true;

            try
            {
                return false;
            }
            finally
            {
                Monitor.Exit(source);
            }
        }

        public static void ParallelForEach<T>(this ICollection<T> source, Action<T> body)
        {
            if (source.IsNullOrEmpty() || body == null)
                return;

            if (source.Count == 1)
            {
                body(source.First());
                return;
            }

            Parallel.ForEach(source, body);
        }

        public static bool IsNullOrEmpty<T>(this ICollection<T> source)
        {
            return source == null || source.Count <= 0;
        }
    }
}