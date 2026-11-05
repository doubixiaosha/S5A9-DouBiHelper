using System;
using System.Diagnostics;

namespace DnfHelper.UI.Services
{
    public sealed class ProcessInfo
    {
        public uint Pid { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public static class ProcessService
    {
        /// <summary>
        /// 按可执行文件名查找进程，忽略大小写，允许传 "ARAD.EXE" 或 "ARAD"。
        /// 找不到返回 null。
        /// </summary>
        public static ProcessInfo FindByExeName(string exeName)
        {
            if (string.IsNullOrEmpty(exeName))
            {
                return null;
            }

            var name = exeName.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ProcessInfo { Pid = (uint)process.Id, Name = process.ProcessName };
                    }
                }
                catch (Exception)
                {
                    // 读取进程名失败的条目直接跳过。
                }
                finally
                {
                    process.Dispose();
                }
            }
            return null;
        }

        public static bool IsAlive(uint pid)
        {
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                {
                    return !process.HasExited;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
