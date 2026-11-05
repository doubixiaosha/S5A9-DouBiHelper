using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Feature
{
    internal sealed class EquipConfig
    {
        internal bool Enabled;
        internal int[] Actions = { 0, 0, 0, 0, 0, 0 }; // 白/蓝/紫/粉/史诗/传说：0 不处理 1 卖物 2 分解
        internal bool[] Exempt = new bool[GameAddresses.EquipSlotCount];

        internal EquipConfig Copy()
        {
            return new EquipConfig
            {
                Enabled = Enabled,
                Actions = (int[])Actions.Clone(),
                Exempt = (bool[])Exempt.Clone(),
            };
        }
    }

    /// <summary>
    /// ALT+F 触发一次装备处理。
    /// </summary>
    internal static class EquipFeature
    {
        private static readonly object ConfigLock = new object();
        private static readonly Worker WorkerThread = new Worker();
        private static EquipConfig _config = new EquipConfig();
        private static int _requested;
        private static int _running;

        private static EquipConfig Snapshot()
        {
            lock (ConfigLock)
            {
                return _config.Copy();
            }
        }

        // 品质 5（异界）不在设计文档六类型内，直接跳过。
        private static int ActionForQuality(EquipConfig config, int quality)
        {
            switch (quality)
            {
                case 0: return config.Actions[0];
                case 1: return config.Actions[1];
                case 2: return config.Actions[2];
                case 3: return config.Actions[3];
                case 4: return config.Actions[4];
                case 6: return config.Actions[5];
                default: return 0;
            }
        }

        private static bool ContainsInheritance(string name)
        {
            return name != null && name.IndexOf("传承", System.StringComparison.Ordinal) >= 0;
        }

        private static void ProcessOnce()
        {
            var config = Snapshot();
            if (!config.Enabled)
            {
                CoreLog.Warn("装备处理 未开启，已忽略");
                return;
            }
            if (GameReader.Player() == 0)
            {
                return;
            }

            var failed = false;

            // 装备区 1..56，协议槽位 = 索引 + 8。
            var equips = GameReader.ScanEquipment();
            foreach (var item in equips)
            {
                if (item.Pointer == 0 || item.Id <= 0)
                {
                    continue;
                }
                if (config.Exempt[item.Slot - 1])
                {
                    continue;
                }
                if (item.Quality == 2 && ContainsInheritance(item.Name))
                {
                    continue; // 紫装传承保留
                }

                var action = ActionForQuality(config, item.Quality);
                if (action == 1)
                {
                    if (!PacketSender.SellItem(item.Slot))
                    {
                        failed = true;
                    }
                }
                else if (action == 2)
                {
                    if (!PacketSender.Disassemble(item.Slot))
                    {
                        failed = true;
                    }
                }
            }

            if (!PacketSender.OrganizeBag())
            {
                failed = true;
            }
            CoreLog.Info(failed ? "装备处理 未完成" : "装备处理 已完成");
        }

        private static void EquipLoop()
        {
            if (Interlocked.Exchange(ref _requested, 0) == 0)
            {
                return;
            }
            Interlocked.Exchange(ref _running, 1);
            try
            {
                ProcessOnce();
            }
            catch (System.Exception)
            {
                CoreLog.Error("装备处理 执行失败");
            }
            Interlocked.Exchange(ref _running, 0);
        }

        internal static void SetEquipConfig(EquipConfig config)
        {
            lock (ConfigLock)
            {
                _config = config ?? new EquipConfig();
            }
            Interlocked.Exchange(ref _requested, 0);
            if (config == null || !config.Enabled)
            {
                WorkerThread.Stop();
                CoreLog.Info("装备处理 已关闭");
                return;
            }
            if (!RemoteProcess.Instance.Attached)
            {
                CoreLog.Warn("未附加游戏，已忽略");
                return;
            }
            if (!WorkerThread.Running)
            {
                WorkerThread.Start(25, EquipLoop);
            }
            CoreLog.Info("装备处理 已开启");
        }

        internal static void TriggerEquipProcess()
        {
            if (!RemoteProcess.Instance.Attached)
            {
                CoreLog.Warn("未附加游戏，已忽略");
                return;
            }
            Interlocked.Exchange(ref _requested, 1);
        }

        internal static bool EquipRunning()
        {
            return Interlocked.CompareExchange(ref _running, 0, 0) != 0 || WorkerThread.Running;
        }

        internal static void ShutdownEquip()
        {
            WorkerThread.Stop();
        }
    }
}
