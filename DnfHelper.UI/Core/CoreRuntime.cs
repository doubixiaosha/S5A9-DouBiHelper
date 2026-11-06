using System;
using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Feature;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core
{
    internal struct StatusSnapshot
    {
        internal bool Attached;
        internal int GameState;
        internal bool InDungeon;
        internal int Level;
        internal int Town;
        internal int Area;
        internal int RoomX;
        internal int RoomY;
        internal string Name;
        internal string Job;
        internal float PosX;
        internal float PosY;
        internal int DungeonId;
        internal string DungeonName;
        internal bool HasBoss;
        internal int BossX;
        internal int BossY;
        internal bool PickupRunning;
        internal bool TraverseRunning;
        internal bool EquipRunning;
        internal int AutoCharacterId;   // 自动读取的角色 ID，0 = 未读到（仅展示用）
    }

    /// <summary>
    /// 进程内入口：UI 直接调用这里，配置下发、状态读取与各功能触发都在本进程内完成。
    /// </summary>
    internal static class CoreRuntime
    {
        internal const string Version = "1.1.0";

        /// <summary>
        /// BUFF / 物品远程调用序列的全局互斥：整个序列（多条 BUFF 或多件物品）期间独占，
        /// 任意时刻只允许一条序列在跑；后到的直接丢弃，不排队、不等待。
        /// </summary>
        private static int _callSequenceBusy;

        internal static bool TryEnterCallSequence(string name)
        {
            if (Interlocked.CompareExchange(ref _callSequenceBusy, 1, 0) != 0)
            {
                CoreLog.Warn(name + " 正在执行，已忽略");
                return false;
            }
            return true;
        }

        internal static void ExitCallSequence()
        {
            Interlocked.Exchange(ref _callSequenceBusy, 0);
        }

        private static void StopAll()
        {
            PickupFeature.ShutdownPickup();
            EquipFeature.ShutdownEquip();
            TraverseFeature.ShutdownTraverse();
            GmFeature.ShutdownGm();
            BuffFeature.Shutdown();
            AutoItemFeature.Shutdown();
            HelperFeature.Shutdown();
            AnnounceFeature.Shutdown();
            Interlocked.Exchange(ref _callSequenceBusy, 0);
        }

        /// <summary>游戏内提示框公告；未附加/未进入角色静默跳过。</summary>
        internal static void Announce(string text)
        {
            try
            {
                AnnounceFeature.Announce(text);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>ALT+G 本地 GM 切换：在权限检查处写 16 字节权限桩。</summary>
        internal static bool ToggleLocalGm(out bool enabled, out string note)
        {
            try
            {
                return GmFeature.Toggle(out enabled, out note);
            }
            catch (Exception)
            {
                enabled = false;
                note = "本地GM 切换异常";
                return false;
            }
        }

        internal static void SetBuff(string types)
        {
            try
            {
                BuffFeature.SetConfig(new BuffConfig
                {
                    Types = types ?? string.Empty,
                });
            }
            catch (Exception)
            {
                CoreLog.Error("BUFF 配置失败");
            }
        }

        internal static void TriggerBuff()
        {
            try
            {
                BuffFeature.TriggerOnce();
            }
            catch (Exception)
            {
            }
        }

        internal static void SetAutoItem(int enabled, string ids)
        {
            try
            {
                AutoItemFeature.SetConfig(new AutoItemConfig
                {
                    Enabled = enabled != 0,
                    Ids = ids ?? string.Empty,
                });
            }
            catch (Exception)
            {
                CoreLog.Error("自动物品 配置失败");
            }
        }

        internal static void TriggerAutoItem()
        {
            try
            {
                AutoItemFeature.TriggerOnce();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>助手开关下发：只记录状态，已附加且状态变化时才发包。</summary>
        internal static void SetHelper(int pickup, int hell)
        {
            try
            {
                HelperFeature.SetConfig(pickup != 0, hell != 0);
            }
            catch (Exception)
            {
                CoreLog.Error("助手 配置失败");
            }
        }

        internal static bool Attach(uint pid, out string error)
        {
            error = string.Empty;
            try
            {
                StopAll();
                RemoteProcess.Instance.Detach();
                if (!RemoteProcess.Instance.Attach(pid, out error))
                {
                    CoreLog.Error("附加失败：" + (string.IsNullOrEmpty(error) ? "未知原因" : error));
                    if (string.IsNullOrEmpty(error))
                    {
                        error = "打开或校验目标进程失败";
                    }
                    return false;
                }
                return true;
            }
            catch (Exception)
            {
                CoreLog.Error("附加失败");
                error = "附加进程异常";
                return false;
            }
        }

        internal static void Detach()
        {
            try
            {
                StopAll();
                RemoteProcess.Instance.Detach();
            }
            catch (Exception)
            {
            }
        }

        internal static bool IsAttached()
        {
            return RemoteProcess.Instance.Attached;
        }

        /// <summary>直读当前游戏状态（复用 GameReader 的 GameState 地址），供延迟触发前的最终校验。</summary>
        internal static int CurrentGameState()
        {
            return RemoteProcess.Instance.Attached ? GameReader.GameState() : 0;
        }

        internal static void SetPickup(int enabled, int mode, int intervalMs)
        {
            try
            {
                var config = new PickupConfig
                {
                    Enabled = enabled != 0,
                    Mode = mode == 2 ? 2 : 1,
                    IntervalMs = intervalMs < 100 ? 100 : intervalMs,
                };
                PickupFeature.SetPickupConfig(config);
            }
            catch (Exception)
            {
                CoreLog.Error("自动拾取 配置失败");
            }
        }

        internal static void SetEquip(int enabled, int[] actions, int[] exempt)
        {
            try
            {
                var config = new EquipConfig { Enabled = enabled != 0 };
                if (actions != null)
                {
                    for (var i = 0; i < 6; i++)
                    {
                        config.Actions[i] = actions[i] >= 0 && actions[i] <= 2 ? actions[i] : 0;
                    }
                }
                if (exempt != null)
                {
                    for (var i = 0; i < GameAddresses.EquipSlotCount; i++)
                    {
                        config.Exempt[i] = exempt[i] != 0;
                    }
                }
                EquipFeature.SetEquipConfig(config);
            }
            catch (Exception)
            {
                CoreLog.Error("装备处理 配置失败");
            }
        }

        internal static void EquipTrigger()
        {
            try
            {
                EquipFeature.TriggerEquipProcess();
            }
            catch (Exception)
            {
            }
        }

        internal static void SetTraverse(int enabled, int mode)
        {
            try
            {
                var config = new TraverseConfig
                {
                    Enabled = enabled != 0,
                    Mode = mode >= 1 && mode <= 4 ? mode : 1,
                };
                TraverseFeature.SetTraverseConfig(config);
            }
            catch (Exception)
            {
                CoreLog.Error("顺图 配置失败");
            }
        }

        internal static void TraverseStep(int dir)
        {
            try
            {
                TraverseFeature.TraverseStep(dir);
            }
            catch (Exception)
            {
            }
        }

        internal static ItemView[] ScanBag()
        {
            try
            {
                return FillSlots(GameReader.ScanBag(), GameAddresses.BagSlotFirst,
                    GameAddresses.BagSlotLast - GameAddresses.BagSlotFirst + 1);
            }
            catch (Exception)
            {
                return new ItemView[0];
            }
        }

        internal static ItemView[] ScanVault()
        {
            try
            {
                return FillSlots(GameReader.ScanVault(), GameAddresses.VaultSlotFirst,
                    GameAddresses.VaultSlotLast - GameAddresses.VaultSlotFirst + 1);
            }
            catch (Exception)
            {
                return new ItemView[0];
            }
        }

        internal static ItemView[] ScanEquip()
        {
            try
            {
                return FillSlots(GameReader.ScanEquipment(), 1, GameAddresses.EquipSlotCount);
            }
            catch (Exception)
            {
                return new ItemView[0];
            }
        }

        internal static bool GetStatus(out StatusSnapshot status)
        {
            status = new StatusSnapshot();
            try
            {
                status.Attached = RemoteProcess.Instance.Attached;
                status.GameState = GameReader.GameState();
                status.InDungeon = GameReader.InDungeon();
                status.Level = GameReader.Level();
                status.Town = GameReader.Town();
                status.Area = GameReader.Area();
                RoomCoord coord;
                var roomOkay = GameReader.ReadCurRoom(out coord);
                status.RoomX = roomOkay ? coord.X : 0;
                status.RoomY = roomOkay ? coord.Y : 0;
                if (status.Attached)
                {
                    // 角色信息与副本/房间信息同一次采样，供界面 1s 轮询直接展示。
                    var character = GameReader.ReadCharacter();
                    status.Name = character.Name;
                    status.Job = character.Job;
                    status.GameState = character.State;
                    status.InDungeon = character.State == 3;
                    status.Level = character.Level;
                    status.Town = character.Town;
                    status.Area = character.Area;
                    status.PosX = character.X;
                    status.PosY = character.Y;
                    status.DungeonId = character.DungeonId;
                    status.DungeonName = character.DungeonName;
                    status.HasBoss = character.HasBoss;
                    status.BossX = character.BossX;
                    status.BossY = character.BossY;
                    status.RoomX = character.HasRoom ? character.RoomX : status.RoomX;
                    status.RoomY = character.HasRoom ? character.RoomY : status.RoomY;
                    // 展示用：与发包用的取值读同一条链路，只做只读采样，不影响发包。
                    status.AutoCharacterId = GameReader.CharacterId();
                }
                status.PickupRunning = PickupFeature.PickupRunning();
                status.TraverseRunning = TraverseFeature.TraverseRunning();
                status.EquipRunning = EquipFeature.EquipRunning();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void Shutdown()
        {
            try
            {
                StopAll();
                RemoteProcess.Instance.Detach();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>固定输出 count 项，缺项补零并带上槽位号。</summary>
        private static ItemView[] FillSlots(System.Collections.Generic.List<ItemView> items, int firstSlot, int count)
        {
            var output = new ItemView[count];
            for (var i = 0; i < count; i++)
            {
                var view = new ItemView { Slot = firstSlot + i, Name = string.Empty };
                if (i < items.Count)
                {
                    view.Id = items[i].Id;
                    view.Quality = items[i].Quality;
                    view.Count = items[i].Count;
                    view.Name = items[i].Name ?? string.Empty;
                }
                output[i] = view;
            }
            return output;
        }
    }
}
