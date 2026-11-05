using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// 远程执行：VirtualAllocEx(≥4KB, RWX) → 写代码 → CreateRemoteThread → Wait → GetExitCodeThread → VirtualFreeEx。
    /// 线程收尾用目标进程 ntdll!RtlExitUserThread：本进程取导出算 RVA，再加目标进程 ntdll 基址。
    /// 同一时刻只允许一条远程线程（见 Execute 内的全局锁），连续调用之间可按需求拉开最小间隔。
    /// </summary>
    internal sealed class RemoteExecutor
    {
        private static readonly RemoteExecutor Shared = new RemoteExecutor();

        private readonly object _lock = new object();
        private uint _exitThreadAddress;
        private uint _resolvedPid;
        private int _lastEndTick;

        private RemoteExecutor()
        {
        }

        internal static RemoteExecutor Instance => Shared;

        /// <param name="minGapMs">
        /// 与上一次远程执行的最小间隔；BUFF / 物品这类连续 CALL 传 50，包发送等高频调用传 0。
        /// 全局锁本身已保证任一时刻只有一条远程线程，此参数只额外拉开时间。
        /// </param>
        internal bool Execute(Asm code, uint timeoutMs, out string error, int minGapMs = 0)
        {
            error = string.Empty;
            var process = RemoteProcess.Instance;
            if (!process.Attached)
            {
                error = "未附加进程";
                return false;
            }
            if (code == null || code.Size == 0)
            {
                error = "空机器码";
                return false;
            }

            var processHandle = NativeApi.OpenProcess(NativeApi.ProcessAllAccess, false, process.Pid);
            if (processHandle == IntPtr.Zero)
            {
                error = "远程执行打开进程失败，错误码 " + Marshal.GetLastWin32Error();
                return false;
            }

            lock (_lock)
            {
                if (_exitThreadAddress == 0 || _resolvedPid != process.Pid)
                {
                    if (!ResolveExitThread(process, processHandle, out error))
                    {
                        NativeApi.CloseHandle(processHandle);
                        return false;
                    }
                }

                // 连续触发（连点热键、BUFF 与物品紧邻）时拉开最小间隔。
                if (minGapMs > 0)
                {
                    var gap = Environment.TickCount - _lastEndTick;
                    if (_lastEndTick != 0 && gap >= 0 && gap < minGapMs)
                    {
                        Thread.Sleep(minGapMs - gap);
                    }
                }

                // 数据区从 16 字节对齐处开始，总长不足 4KB 时补到 4KB。
                var dataStart = (code.Size + 15) & ~15;
                var totalSize = dataStart < 0x1000 ? 0x1000u : (uint)dataStart;
                var remote = (uint)(long)NativeApi.VirtualAllocEx(processHandle, IntPtr.Zero, totalSize,
                    NativeApi.MemCommit | NativeApi.MemReserve, NativeApi.PageExecuteReadWrite);
                if (remote == 0)
                {
                    NativeApi.CloseHandle(processHandle);
                    error = "远程内存分配失败，错误码 " + Marshal.GetLastWin32Error();
                    return false;
                }

                // 退出序列：push 0; mov eax, exitFn; call eax（不加 int3）。
                var full = new Asm();
                full.Raw(code.Build());
                full.Push(0);
                full.MovEax(_exitThreadAddress);
                full.CallEax();

                var machineCode = full.Build();
                if (!NativeApi.WriteProcessMemory(processHandle, new IntPtr(unchecked((int)remote)),
                        machineCode, (uint)machineCode.Length, out _))
                {
                    NativeApi.VirtualFreeEx(processHandle, new IntPtr(unchecked((int)remote)), 0, NativeApi.MemRelease);
                    NativeApi.CloseHandle(processHandle);
                    error = "远程代码写入失败，错误码 " + Marshal.GetLastWin32Error();
                    return false;
                }
                NativeApi.FlushInstructionCache(processHandle, new IntPtr(unchecked((int)remote)),
                    (uint)machineCode.Length);

                var thread = NativeApi.CreateRemoteThread(processHandle, IntPtr.Zero, 0,
                    new IntPtr(unchecked((int)remote)), IntPtr.Zero, 0, IntPtr.Zero);
                if (thread == IntPtr.Zero)
                {
                    NativeApi.VirtualFreeEx(processHandle, new IntPtr(unchecked((int)remote)), 0, NativeApi.MemRelease);
                    NativeApi.CloseHandle(processHandle);
                    error = "远程线程创建失败，错误码 " + Marshal.GetLastWin32Error();
                    return false;
                }

                var wait = NativeApi.WaitForSingleObject(thread, timeoutMs);
                uint exitCode = 0;
                NativeApi.GetExitCodeThread(thread, out exitCode);
                var okay = true;
                if (wait == NativeApi.WaitTimeout)
                {
                    okay = false;
                    error = "远程线程执行超时";
                    // 线程可能仍在游戏函数内：再等它真正退出，避免下一轮把新线程叠在旧线程上；
                    // 最多再等 5s，仍不退出则保留内存不释放（防止释放正在执行的代码页）。
                    wait = NativeApi.WaitForSingleObject(thread, 5000);
                }
                else if (wait != NativeApi.WaitObject0)
                {
                    okay = false;
                    error = "远程线程等待失败，错误码 " + Marshal.GetLastWin32Error();
                }

                NativeApi.CloseHandle(thread);
                if (wait == NativeApi.WaitObject0)
                {
                    NativeApi.VirtualFreeEx(processHandle, new IntPtr(unchecked((int)remote)), 0,
                        NativeApi.MemRelease);
                }
                NativeApi.CloseHandle(processHandle);
                _lastEndTick = Environment.TickCount;
                return okay;
            }
        }

        /// <summary>
        /// 解析 RtlExitUserThread：本进程 ntdll 的 GetProcAddress 结果减去本进程 ntdll 基址得到 RVA，
        /// 再加目标进程 ntdll 基址；RVA 上界按 16MB 校验，防止误算。
        /// </summary>
        private bool ResolveExitThread(RemoteProcess process, IntPtr processHandle, out string error)
        {
            error = string.Empty;
            var localNtdll = NativeApi.GetModuleHandle("ntdll.dll");
            if (localNtdll == IntPtr.Zero)
            {
                error = "未定位 RtlExitUserThread：本进程 GetModuleHandle(ntdll.dll) 失败，错误码 " +
                        Marshal.GetLastWin32Error();
                return false;
            }

            var localProc = NativeApi.GetProcAddress(localNtdll, "RtlExitUserThread");
            var localBase = (uint)(long)localNtdll;
            var rva = localProc == IntPtr.Zero ? 0u : (uint)(long)localProc - localBase;
            var remoteNtdll = process.ModuleBase("ntdll.dll");

            if (remoteNtdll == 0 || rva == 0 || rva >= 0x1000000u)
            {
                error = "未定位 RtlExitUserThread（目标 ntdll 模块或导出不可用）";
                if (processHandle == IntPtr.Zero)
                {
                    error += "，进程句柄为空";
                }
                return false;
            }

            _exitThreadAddress = remoteNtdll + rva;
            _resolvedPid = process.Pid;
            return true;
        }
    }
}
