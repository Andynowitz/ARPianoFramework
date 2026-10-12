using System;
using System.Threading;
using UnityEngine;

namespace FFmpegUnityBind2.Utils
{
    public static class UnityMainThread
    {
        private static SynchronizationContext context;
        private static int mainThreadId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            context = SynchronizationContext.Current;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static void Invoke(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                action();
                return;
            }

            if (context == null)
                throw new InvalidOperationException(
                    "Unity main-thread SynchronizationContext has not been initialized.");

            context.Post(_ => action(), null);
        }
    }
}
