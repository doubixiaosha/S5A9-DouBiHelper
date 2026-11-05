using System;

namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// 日志回调由 UI 注入，任何线程都可调用，订阅方自行转投 UI 线程。
    /// </summary>
    internal static class CoreLog
    {
        internal static event Action<int, string> Message;

        internal static void Info(string message)
        {
            Write(0, message);
        }

        internal static void Warn(string message)
        {
            Write(1, message);
        }

        internal static void Error(string message)
        {
            Write(2, message);
        }

        internal static void Write(int level, string message)
        {
            var handler = Message;
            if (handler != null)
            {
                handler(level, message);
            }
            NativeApi.OutputDebugString("[DouBiHelper] " + message + "\n");
        }
    }
}
