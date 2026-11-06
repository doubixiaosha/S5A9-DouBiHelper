using System;

namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// 内部诊断日志：统一输出到调试器（OutputDebugString），不再进入界面。
    /// </summary>
    internal static class CoreLog
    {
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
            NativeApi.OutputDebugString("[DouBi] " + message + "\n");
        }
    }
}
