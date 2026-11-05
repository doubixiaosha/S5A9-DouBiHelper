using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Feature
{
    /// <summary>
    /// 本地 GM：在权限检查处写入 16 字节权限桩（地址 kGmPermissionMode / RoleBase）。
    /// 在 kGmPermissionMode 写 16 字节权限桩：
    ///   cmp ecx,[角色对象指针槽] / jz +3 / xor al,al / ret / mov al,1 / ret / int3;int3
    /// 使权限检查（ecx = 被检查对象）对当前角色恒返回 1；关闭时写回原字节。
    /// </summary>
    internal static class GmFeature
    {
        private static readonly BytePatch GmPatch = new BytePatch();
        private static int _patchReady;
        private static int _enabled;

        internal static bool Enabled => Volatile.Read(ref _enabled) != 0;

        /// <summary>切换本地 GM。note 为空表示成功；失败或警告时给出可直接阅读的原因。</summary>
        internal static bool Toggle(out bool enabled, out string note)
        {
            enabled = Enabled;
            note = string.Empty;
            var process = RemoteProcess.Instance;
            if (!process.Attached)
            {
                note = "未附加游戏";
                return false;
            }

            if (Interlocked.CompareExchange(ref _patchReady, 1, 0) == 0)
            {
                GmPatch.SetTarget(GameAddresses.GmPermissionMode, BuildPatch(), 16);
            }

            var target = !Enabled;
            if (!GmPatch.Apply(target))
            {
                note = "本地GM 切换失败";
                return false;
            }

            Volatile.Write(ref _enabled, target ? 1 : 0);
            enabled = target;

            // 权限桩拿 [角色对象指针槽] 做比较；槽为空时补丁虽写入但不会命中，给出原因。
            var role = process.ReadDword(GameAddresses.RoleBase);
            if (role == 0 || role == 0xFFFFFFFFu)
            {
                note = "尚未进入角色，GM 权限可能不生效";
            }
            return true;
        }

        /// <summary>还原补丁（附加断开/退出时无条件调用，与是否按过热键无关）。</summary>
        internal static void ShutdownGm()
        {
            if (Enabled)
            {
                GmPatch.Apply(false);
            }
            GmPatch.Restore();
            GmPatch.Forget();
            Interlocked.Exchange(ref _patchReady, 0);
            Volatile.Write(ref _enabled, 0);
        }

        /// <summary>16 字节权限桩，角色基址立即数写入 cmp 指令的位移部分。</summary>
        private static byte[] BuildPatch()
        {
            var role = GameAddresses.RoleBase;
            var bytes = new byte[16];
            bytes[0] = 0x3B; // cmp ecx,[imm32]
            bytes[1] = 0x0D;
            bytes[2] = (byte)(role & 0xFF);
            bytes[3] = (byte)((role >> 8) & 0xFF);
            bytes[4] = (byte)((role >> 16) & 0xFF);
            bytes[5] = (byte)((role >> 24) & 0xFF);
            bytes[6] = 0x74; // jz +3
            bytes[7] = 0x03;
            bytes[8] = 0x30; // xor al,al / ret
            bytes[9] = 0xC0;
            bytes[10] = 0xC3;
            bytes[11] = 0xB0; // mov al,1 / ret / int3;int3
            bytes[12] = 0x01;
            bytes[13] = 0xC3;
            bytes[14] = 0xCC;
            bytes[15] = 0xCC;
            return bytes;
        }
    }
}
