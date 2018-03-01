using System;
using System.Collections.Generic;
using System.Threading;

namespace GZipTest
{
    /// <summary>
    /// Реализация примитивного пула потоков (Стандартный по условиями использовать нельзя)
    /// </summary>
    internal sealed class SimpleThreadPool : IDisposable
    {
        private readonly int _poolSize = Environment.ProcessorCount * 4;

        private readonly LinkedList<Thread> _workers;
        private readonly LinkedList<Action> _tasks = new LinkedList<Action>();
        private bool _disallowAdd;
        private bool _disposed;
        
        /// <summary>
        /// ctor
        /// </summary>
        public SimpleThreadPool()
        {
            _workers = new LinkedList<Thread>();
            for (var i = 0; i < _poolSize; ++i)
            {
                var worker = new Thread(ThreadLoop);
                worker.Start();
                _workers.AddLast(worker);
            }
        }

        /// <summary>
        /// dtor
        /// </summary>
        ~SimpleThreadPool()
        {
            if(!_disposed)
                Dispose();
        }
        
        /// <inheritdoc cref="IDisposable.Dispose"/>
        public void Dispose()
        {
            var waitForThreads = false;
            lock (_tasks)
            {
                if (!_disposed)
                {
                    GC.SuppressFinalize(this);

                    _disallowAdd = true;
                    while (_tasks.Count > 0)
                    {
                        Monitor.Wait(_tasks);
                    }

                    _disposed = true;
                    Monitor.PulseAll(_tasks);
                    waitForThreads = true;
                }
            }

            if (!waitForThreads) 
                return;

            foreach (var worker in _workers)
                worker.Join();
        }

        /// <summary>
        /// Добавляет задание на выполнение в потоке из пула
        /// </summary>
        /// <param name="task">Задание</param>
        public void RunTask(Action task)
        {
            lock (_tasks)
            {
                if (_disallowAdd)
                    throw new InvalidOperationException("Пул в процессе уничтожения");
                if (_disposed)
                    throw new ObjectDisposedException("Пул уничтожен");

                _tasks.AddLast(task);

                Monitor.PulseAll(_tasks);
            }
        }
        
        private void ThreadLoop()
        {
            while (true)
            {
                //Сначала встанем в очередь за заданием
                Action task;
                lock (_tasks)
                {
                    while (true)
                    {
                        if (_disposed)
                            return;

                        if (null != _workers.First && ReferenceEquals(Thread.CurrentThread, _workers.First.Value) && _tasks.Count > 0)
                        {
                            task = _tasks.First.Value;
                            _tasks.RemoveFirst();
                            _workers.RemoveFirst();
                            Monitor.PulseAll(_tasks);
                            break;
                        }
                        Monitor.Wait(_tasks);
                    }
                }

                //Выполним его
                task();

                //Добавим себя в список свободных
                lock (_tasks)
                {
                    _workers.AddLast(Thread.CurrentThread);
                }
            }
        }
    }
}
