using System;
using System.Threading;

namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// 周期任务线程：间隔可随时修改，Stop 可中断等待；body 只在工作线程执行。
    /// </summary>
    internal sealed class Worker
    {
        private readonly object _lock = new object();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private Thread _thread;
        private Action _body;
        private bool _stop;
        private bool _running;
        private int _intervalMs = 500;

        internal bool Running
        {
            get
            {
                lock (_lock)
                {
                    return _running;
                }
            }
        }

        internal void Start(int intervalMs, Action body)
        {
            Stop();
            lock (_lock)
            {
                _body = body;
                _intervalMs = intervalMs < 10 ? 10 : intervalMs;
                _stop = false;
                _running = true;
            }
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "DouBiWorker",
            };
            _thread.Start();
        }

        internal void Stop()
        {
            Thread thread;
            lock (_lock)
            {
                if (!_running)
                {
                    return;
                }
                _stop = true;
                thread = _thread;
            }
            _signal.Set();
            if (thread != null)
            {
                thread.Join();
            }
            lock (_lock)
            {
                _running = false;
                _body = null;
            }
        }

        internal void SetInterval(int intervalMs)
        {
            lock (_lock)
            {
                _intervalMs = intervalMs < 10 ? 10 : intervalMs;
            }
            _signal.Set();
        }

        private void Loop()
        {
            while (true)
            {
                int interval;
                lock (_lock)
                {
                    if (_stop)
                    {
                        break;
                    }
                    interval = _intervalMs;
                }

                _signal.WaitOne(interval);

                Action body;
                lock (_lock)
                {
                    if (_stop)
                    {
                        break;
                    }
                    body = _body;
                }

                if (body != null)
                {
                    try
                    {
                        body();
                    }
                    catch (Exception)
                    {
                        CoreLog.Error("执行失败");
                    }
                }
            }

            lock (_lock)
            {
                _running = false;
            }
        }
    }
}
