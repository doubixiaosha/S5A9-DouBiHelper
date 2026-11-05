using System;
using System.Runtime.InteropServices;

namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// 远程目标进程的内存访问入口。
    /// 所有功能线程共用同一连接，Detach 前必须先停各功能线程。
    /// </summary>
    internal sealed class RemoteProcess
    {
        private static readonly RemoteProcess Shared = new RemoteProcess();

        private readonly object _lock = new object();
        private IntPtr _handle = IntPtr.Zero;
        private uint _pid;

        private RemoteProcess()
        {
        }

        internal static RemoteProcess Instance => Shared;

        internal bool Attach(uint pid, out string error)
        {
            error = string.Empty;
            Detach();
            NativeApi.EnableDebugPrivilege();

            var handle = NativeApi.OpenProcess(
                NativeApi.ProcessVmRead | NativeApi.ProcessVmWrite | NativeApi.ProcessVmOperation |
                NativeApi.ProcessQueryInformation | NativeApi.ProcessCreateThread,
                false, pid);
            if (handle == IntPtr.Zero)
            {
                handle = NativeApi.OpenProcess(NativeApi.ProcessAllAccess, false, pid);
            }
            if (handle == IntPtr.Zero)
            {
                error = "打开游戏进程失败";
                return false;
            }

            // 32 位宿主只能处理 32 位目标；64 位系统上 32 位进程会返回 TRUE。
            var is64Os = NativeApi.IsWow64Process(NativeApi.GetCurrentProcess(), out var selfWow64) && selfWow64;
            if (is64Os && NativeApi.IsWow64Process(handle, out var targetWow64) && !targetWow64)
            {
                NativeApi.CloseHandle(handle);
                error = "游戏进程不是 32 位";
                return false;
            }

            lock (_lock)
            {
                _handle = handle;
                _pid = pid;
            }

            CoreLog.Info("已附加游戏进程");
            return true;
        }

        internal void Detach()
        {
            IntPtr handle;
            lock (_lock)
            {
                handle = _handle;
                _handle = IntPtr.Zero;
                _pid = 0;
            }
            if (handle != IntPtr.Zero)
            {
                NativeApi.CloseHandle(handle);
            }
        }

        internal bool Attached
        {
            get
            {
                lock (_lock)
                {
                    return _handle != IntPtr.Zero;
                }
            }
        }

        internal uint Pid
        {
            get
            {
                lock (_lock)
                {
                    return _pid;
                }
            }
        }

        internal bool ReadBytes(uint address, byte[] buffer, int size)
        {
            if (address == 0 || buffer == null || size <= 0)
            {
                return false;
            }

            lock (_lock)
            {
                return _handle != IntPtr.Zero &&
                       NativeApi.ReadProcessMemory(_handle, new IntPtr(address), buffer, (uint)size, out _);
            }
        }

        internal uint ReadDword(uint address, uint fallback = 0)
        {
            var buffer = new byte[4];
            return ReadBytes(address, buffer, 4) ? BitConverter.ToUInt32(buffer, 0) : fallback;
        }

        internal float ReadFloat(uint address, float fallback = 0.0f)
        {
            var buffer = new byte[4];
            return ReadBytes(address, buffer, 4) ? BitConverter.ToSingle(buffer, 0) : fallback;
        }

        internal bool ReadWideString(uint address, int maxChars, out string value)
        {
            value = string.Empty;
            if (address == 0 || maxChars <= 0)
            {
                return false;
            }
            if (maxChars > 128)
            {
                maxChars = 128;
            }

            var buffer = new byte[maxChars * 2];
            if (!ReadBytes(address, buffer, buffer.Length))
            {
                return false;
            }

            var chars = new char[maxChars];
            for (var i = 0; i < maxChars; i++)
            {
                chars[i] = (char)BitConverter.ToUInt16(buffer, i * 2);
            }

            var length = Array.IndexOf(chars, '\0');
            if (length < 0)
            {
                length = maxChars - 1;
            }
            value = new string(chars, 0, length);
            return true;
        }

        internal bool WriteBytes(uint address, byte[] data, int size)
        {
            if (address == 0 || data == null || size <= 0)
            {
                return false;
            }

            lock (_lock)
            {
                if (_handle == IntPtr.Zero)
                {
                    return false;
                }

                var protectOkay = NativeApi.VirtualProtectEx(_handle, new IntPtr(address), (uint)size,
                    NativeApi.PageExecuteReadWrite, out var oldProtect);
                var okay = NativeApi.WriteProcessMemory(_handle, new IntPtr(address), data, (uint)size, out _);
                if (protectOkay)
                {
                    NativeApi.VirtualProtectEx(_handle, new IntPtr(address), (uint)size, oldProtect, out _);
                }
                return okay;
            }
        }

        internal bool WriteDword(uint address, uint value)
        {
            return WriteBytes(address, BitConverter.GetBytes(value), 4);
        }

        internal bool WriteFloat(uint address, float value)
        {
            return WriteBytes(address, BitConverter.GetBytes(value), 4);
        }

        /// <summary>在目标进程申请一块长期持有的内存（公告文本缓冲区用）。</summary>
        internal uint AllocateRemote(uint size, uint protect)
        {
            if (size == 0)
            {
                return 0;
            }

            lock (_lock)
            {
                if (_handle == IntPtr.Zero)
                {
                    return 0;
                }
                return (uint)(long)NativeApi.VirtualAllocEx(_handle, IntPtr.Zero, size,
                    NativeApi.MemCommit | NativeApi.MemReserve, protect);
            }
        }

        /// <summary>释放 AllocateRemote 申请的内存（断开/退出时调用）。</summary>
        internal void FreeRemote(uint address)
        {
            if (address == 0)
            {
                return;
            }

            lock (_lock)
            {
                if (_handle == IntPtr.Zero)
                {
                    return;
                }
                NativeApi.VirtualFreeEx(_handle, new IntPtr(unchecked((int)address)), 0, NativeApi.MemRelease);
            }
        }

        internal bool IsReadable(uint address, int size)
        {
            if (address == 0)
            {
                return false;
            }

            lock (_lock)
            {
                if (_handle == IntPtr.Zero)
                {
                    return false;
                }
                if (NativeApi.VirtualQueryEx(_handle, new IntPtr(address), out var info,
                        (uint)Marshal.SizeOf(typeof(NativeApi.MemoryBasicInformation))) == 0)
                {
                    return false;
                }

                var protect = info.Protect;
                var pageOkay = protect != NativeApi.PageNoAccess && protect != 0 &&
                               (info.State & NativeApi.MemCommit) != 0;
                var offset = address - (uint)(long)info.BaseAddress;
                return pageOkay && offset + size <= info.RegionSize;
            }
        }

        /// <summary>
        /// 取目标进程已加载模块的基址：
        /// EnumProcessModulesEx + GetModuleFileNameEx 按文件名匹配。
        /// </summary>
        internal uint ModuleBase(string moduleName)
        {
            IntPtr handle;
            lock (_lock)
            {
                handle = _handle;
            }
            if (handle == IntPtr.Zero)
            {
                return 0;
            }

            uint needed;
            if (!NativeApi.EnumProcessModulesEx(handle, null, 0, out needed, NativeApi.ListModulesAll))
            {
                return 0;
            }
            var count = (int)(needed / (uint)IntPtr.Size);
            if (count <= 0 || count > 4096)
            {
                return 0;
            }

            var modules = new IntPtr[count];
            if (!NativeApi.EnumProcessModulesEx(handle, modules, needed, out needed, NativeApi.ListModulesAll))
            {
                return 0;
            }

            foreach (var module in modules)
            {
                if (module == IntPtr.Zero)
                {
                    continue;
                }
                var name = new System.Text.StringBuilder(260);
                NativeApi.GetModuleFileNameEx(handle, module, name, 260);
                if (string.Equals(System.IO.Path.GetFileName(name.ToString()), moduleName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return (uint)(long)module;
                }
            }
            return 0;
        }
    }
}
