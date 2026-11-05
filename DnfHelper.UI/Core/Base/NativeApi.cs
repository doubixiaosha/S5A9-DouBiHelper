using System;
using System.Runtime.InteropServices;

namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// Win32 调用的进程内 P/Invoke 封装。
    /// 所有句柄与结构均按 32 位布局声明（本 exe 固定 x86，禁止 AnyCPU 运行）。
    /// </summary>
    internal static class NativeApi
    {
        internal const uint ProcessVmOperation = 0x0008;
        internal const uint ProcessVmRead = 0x0010;
        internal const uint ProcessVmWrite = 0x0020;
        internal const uint ProcessQueryInformation = 0x0400;
        internal const uint ProcessCreateThread = 0x0002;
        internal const uint ProcessAllAccess = 0x001F0FFF;

        internal const uint PageNoAccess = 0x01;
        internal const uint PageReadWrite = 0x04;
        internal const uint PageExecuteReadWrite = 0x40;
        internal const uint MemCommit = 0x1000;
        internal const uint MemReserve = 0x2000;
        internal const uint MemRelease = 0x8000;

        internal const uint WaitObject0 = 0x00000000;
        internal const uint WaitTimeout = 0x00000102;

        internal const uint ListModulesAll = 0x00000003;

        internal const uint TokenAdjustPrivileges = 0x0020;
        internal const uint TokenQuery = 0x0008;
        internal const uint SePrivilegeEnabled = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public uint RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public Luid Luid;
            public uint Attributes;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint pid);

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, uint size, out IntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, uint size, out IntPtr bytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool VirtualProtectEx(IntPtr process, IntPtr address, uint size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool VirtualFreeEx(IntPtr process, IntPtr address, uint size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr threadAttributes, uint stackSize, IntPtr startAddress, IntPtr parameter, uint creationFlags, IntPtr threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool FlushInstructionCache(IntPtr process, IntPtr address, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWow64Process(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool wow64);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        internal static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("psapi.dll", SetLastError = true)]
        internal static extern bool EnumProcessModulesEx(IntPtr process, IntPtr[] modules, uint size,
            out uint needed, uint filter);

        [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint GetModuleFileNameEx(IntPtr process, IntPtr module,
            System.Text.StringBuilder name, uint size);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(string systemName, string privilegeName, out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "OutputDebugStringW")]
        internal static extern void OutputDebugString(string text);

        /// <summary>开启当前进程的调试特权。</summary>
        internal static bool EnablePrivilege(string privilegeName)
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out token))
            {
                return false;
            }

            var okay = LookupPrivilegeValue(null, privilegeName, out var luid);
            if (okay)
            {
                var privileges = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SePrivilegeEnabled,
                };
                okay = AdjustTokenPrivileges(token, false, ref privileges, (uint)Marshal.SizeOf(typeof(TokenPrivileges)), IntPtr.Zero, IntPtr.Zero);
            }

            CloseHandle(token);
            return okay;
        }

        internal static bool EnableDebugPrivilege()
        {
            return EnablePrivilege("SeDebugPrivilege");
        }
    }
}
