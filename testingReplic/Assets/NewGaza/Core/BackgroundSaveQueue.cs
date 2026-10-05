using System;
using System.Threading.Tasks;

namespace NewGaza.Core
{
    // One writer and one latest pending snapshot: no overlapping .tmp writes or unbounded jobs.
    public sealed class BackgroundSaveQueue<T> where T : class
    {
        private readonly object gate = new object();
        private readonly Action<T> write;
        private T pending;
        private Task worker;
        private Exception lastFailure, unreportedFailure;
        public BackgroundSaveQueue(Action<T> write) { this.write = write ?? throw new ArgumentNullException(nameof(write)); }
        public void Enqueue(T snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (gate)
            {
                pending = snapshot;
                if (worker == null) worker = Task.Run(Drain);
            }
        }
        private void Drain()
        {
            while (true)
            {
                T snapshot;
                lock (gate)
                {
                    snapshot = pending; pending = null;
                    if (snapshot == null) { worker = null; return; }
                }
                try { write(snapshot); lock (gate) lastFailure = null; }
                catch (Exception error) { lock (gate) lastFailure = unreportedFailure = error; }
            }
        }
        public Exception TakeFailure()
        {
            lock (gate) { var result = unreportedFailure; unreportedFailure = null; return result; }
        }
        public void Flush()
        {
            while (true)
            {
                Task running;
                lock (gate)
                {
                    running = worker;
                    if (running == null)
                    {
                        if (lastFailure != null) throw new InvalidOperationException("Background save failed.", lastFailure);
                        return;
                    }
                }
                running.GetAwaiter().GetResult();
            }
        }
    }
}