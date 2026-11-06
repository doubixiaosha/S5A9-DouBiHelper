using System;
using DnfHelper.UI.Core;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Services
{
    [Serializable]
    public struct CoreItemView
    {
        public int Slot;
        public int Id;
        public int Quality;
        public int Count;
        public string Name;
    }

    [Serializable]
    public struct CoreStatus
    {
        public int Attached;
        public int GameState;
        public int InDungeon;
        public int Level;
        public int Town;
        public int Area;
        public int RoomX;
        public int RoomY;
        public string Name;
        public string Job;
        public float PosX;
        public float PosY;
        public int DungeonId;
        public string DungeonName;
        public int HasBoss;
        public int BossX;
        public int BossY;
        public int PickupRunning;
        public int TraverseRunning;
        public int EquipRunning;
        public int AutoCharacterId;
    }

    /// <summary>
    /// 内部运行时门面：全部能力都在 <see cref="CoreRuntime"/> 内以 C# 实现，
    /// 不加载外部模块，也不释放嵌入资源。
    /// </summary>
    public static class CoreBridge
    {
        public static bool Ready { get; private set; }

        public static string Version => CoreRuntime.Version;

        public static bool Start(out string error)
        {
            error = string.Empty;
            if (Ready)
            {
                return true;
            }
            if (IntPtr.Size != 4)
            {
                error = "必须以 32 位进程运行（当前进程不是 x86）";
                return false;
            }

            Ready = true;
            return true;
        }

        public static void Shutdown()
        {
            if (!Ready)
            {
                return;
            }
            try
            {
                CoreRuntime.Shutdown();
            }
            catch (Exception)
            {
                // 退出路径上的异常不再向上抛。
            }
            Ready = false;
        }

        public static bool Attach(uint pid, out string error)
        {
            if (!Ready)
            {
                error = "内部运行时未初始化";
                return false;
            }
            return CoreRuntime.Attach(pid, out error);
        }

        public static void Detach()
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.Detach();
        }

        public static bool IsAttached()
        {
            return Ready && CoreRuntime.IsAttached();
        }

        public static void SetPickup(bool enabled, int mode, int intervalMs)
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.SetPickup(enabled ? 1 : 0, mode, intervalMs);
        }

        public static void SetEquip(bool enabled, int[] actions, int[] exempt)
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.SetEquip(enabled ? 1 : 0, actions, exempt);
        }

        public static void EquipTrigger()
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.EquipTrigger();
        }

        public static void SetTraverse(bool enabled, int mode)
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.SetTraverse(enabled ? 1 : 0, mode);
        }

        public static void TraverseStep(int dir)
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.TraverseStep(dir);
        }

        public static void SetBuff(string types)
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.SetBuff(types);
        }

        public static void TriggerBuff()
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.TriggerBuff();
        }

        public static void SetAutoItem(bool enabled, string ids)
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.SetAutoItem(enabled ? 1 : 0, ids);
        }

        public static void Announce(string text)
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.Announce(text);
        }

        public static void TriggerAutoItem()
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.TriggerAutoItem();
        }

        /// <summary>助手开关下发；角色 ID 由内部运行时自动读取。</summary>
        public static void SetHelper(bool pickup, bool hell)
        {
            if (!Ready)
            {
                return;
            }
            CoreRuntime.SetHelper(pickup ? 1 : 0, hell ? 1 : 0);
        }

        /// <summary>本地 GM 切换；返回 false 时 note 说明原因。</summary>
        public static bool ToggleLocalGm(out bool enabled, out string note)
        {
            enabled = false;
            note = "内部运行时未初始化";
            if (!Ready)
            {
                return false;
            }
            return CoreRuntime.ToggleLocalGm(out enabled, out note);
        }

        public static CoreItemView[] ScanBag()
        {
            return Ready ? Convert(CoreRuntime.ScanBag()) : new CoreItemView[0];
        }

        public static CoreItemView[] ScanVault()
        {
            return Ready ? Convert(CoreRuntime.ScanVault()) : new CoreItemView[0];
        }

        public static CoreItemView[] ScanEquip()
        {
            return Ready ? Convert(CoreRuntime.ScanEquip()) : new CoreItemView[0];
        }

        /// <summary>只读当前游戏状态（0 未进入/1-2 城镇/3 副本），用于延迟触发前的最终校验。</summary>
        public static int CurrentGameState()
        {
            return Ready ? CoreRuntime.CurrentGameState() : 0;
        }

        public static bool TryGetStatus(out CoreStatus status)
        {
            status = default(CoreStatus);
            if (!Ready)
            {
                return false;
            }

            StatusSnapshot snapshot;
            if (!CoreRuntime.GetStatus(out snapshot))
            {
                return false;
            }

            status.Attached = snapshot.Attached ? 1 : 0;
            status.GameState = snapshot.GameState;
            status.InDungeon = snapshot.InDungeon ? 1 : 0;
            status.Level = snapshot.Level;
            status.Town = snapshot.Town;
            status.Area = snapshot.Area;
            status.RoomX = snapshot.RoomX;
            status.RoomY = snapshot.RoomY;
            status.Name = snapshot.Name ?? string.Empty;
            status.Job = snapshot.Job ?? string.Empty;
            status.PosX = snapshot.PosX;
            status.PosY = snapshot.PosY;
            status.DungeonId = snapshot.DungeonId;
            status.DungeonName = snapshot.DungeonName ?? string.Empty;
            status.HasBoss = snapshot.HasBoss ? 1 : 0;
            status.BossX = snapshot.BossX;
            status.BossY = snapshot.BossY;
            status.PickupRunning = snapshot.PickupRunning ? 1 : 0;
            status.TraverseRunning = snapshot.TraverseRunning ? 1 : 0;
            status.EquipRunning = snapshot.EquipRunning ? 1 : 0;
            status.AutoCharacterId = snapshot.AutoCharacterId;
            return true;
        }

        private static CoreItemView[] Convert(ItemView[] items)
        {
            if (items == null || items.Length == 0)
            {
                return new CoreItemView[0];
            }

            var result = new CoreItemView[items.Length];
            for (var i = 0; i < items.Length; i++)
            {
                result[i] = new CoreItemView
                {
                    Slot = items[i].Slot,
                    Id = items[i].Id,
                    Quality = items[i].Quality,
                    Count = items[i].Count,
                    Name = ClampName(items[i].Name),
                };
            }
            return result;
        }

        /// <summary>把名称写入 wchar_t[64]，保持 63 字符的截断。</summary>
        private static string ClampName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            return name.Length > 63 ? name.Substring(0, 63) : name;
        }
    }
}
