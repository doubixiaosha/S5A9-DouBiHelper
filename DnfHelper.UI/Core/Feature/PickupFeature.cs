using System;
using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Feature
{
    internal sealed class PickupConfig
    {
        internal bool Enabled;
        internal int Mode = 1; // 1=吸物拾取，2=自动入包
        internal int IntervalMs = 500;

        internal PickupConfig Copy()
        {
            return new PickupConfig
            {
                Enabled = Enabled,
                Mode = Mode,
                IntervalMs = IntervalMs,
            };
        }
    }

    /// <summary>
    /// 吸物拾取与自动入包。
    /// </summary>
    internal static class PickupFeature
    {
        private const int MaxPerRound = 64;

        private static readonly object ConfigLock = new object();
        private static readonly Worker WorkerThread = new Worker();
        private static readonly BytePatch AutoPickPatch = new BytePatch();
        private static PickupConfig _config = new PickupConfig();
        private static int _patchReady;

        private static PickupConfig Snapshot()
        {
            lock (ConfigLock)
            {
                return _config.Copy();
            }
        }

        private static bool ApplyAutoPick(bool enabled)
        {
            if (Interlocked.CompareExchange(ref _patchReady, 1, 0) == 0)
            {
                AutoPickPatch.SetTarget(GameAddresses.AutoPickFlag, new byte[] { 0x34, 0x01 }, 2);
            }
            return AutoPickPatch.Apply(enabled);
        }

        // 吸物：把掉落物的坐标改为人物坐标，游戏自身完成吸附。
        private static bool SuckToPlayer(uint pointer, float x, float y)
        {
            var block = RemoteProcess.Instance.ReadDword(pointer + GameAddresses.OffEntityDir);
            if (block == 0)
            {
                return false;
            }
            return RemoteProcess.Instance.WriteFloat(block + GameAddresses.OffDirCoord, x) &&
                   RemoteProcess.Instance.WriteFloat(block + GameAddresses.OffDirCoord + 4, y);
        }

        private static void PickupOnce()
        {
            var config = Snapshot();

            // 每轮先判状态：不在副本(3) 就静默返回——不写补丁、不发包、不打日志。
            if (!GameReader.InDungeon())
            {
                ApplyAutoPick(false);
                return;
            }
            if (GameReader.Player() == 0)
            {
                return;
            }

            // 吸物模式依赖游戏内自动捡物标志，其余模式必须撤掉。
            ApplyAutoPick(config.Mode == 1);

            var px = GameReader.PlayerX();
            var py = GameReader.PlayerY();
            if (config.Mode == 1 && px == 0.0f && py == 0.0f)
            {
                return;
            }

            var items = GameReader.CollectItems(MaxPerRound);
            if (items.Count == 0)
            {
                return;
            }

            var handled = 0;
            foreach (var pointer in items)
            {
                var drop = GameReader.MakeDrop(pointer);

                if (config.Mode == 1)
                {
                    if (SuckToPlayer(pointer, px, py))
                    {
                        ++handled;
                    }
                    continue;
                }

                if (drop.Key < 0)
                {
                    continue;
                }
                float ix;
                float iy;
                if (!GameReader.DropCoord(drop, out ix, out iy))
                {
                    continue;
                }
                if (PacketSender.PickupItemAt(drop.Key, (int)px, (int)py, (int)ix, (int)iy))
                {
                    ++handled;
                }
            }
        }

        internal static void SetPickupConfig(PickupConfig config)
        {
            lock (ConfigLock)
            {
                _config = config ?? new PickupConfig();
            }

            if (config == null || !config.Enabled)
            {
                WorkerThread.Stop();
                ApplyAutoPick(false);
                CoreLog.Info("自动拾取 已关闭");
                return;
            }
            if (!RemoteProcess.Instance.Attached)
            {
                CoreLog.Warn("未附加游戏，已忽略");
                return;
            }

            // 补丁不在这里打：只有进入副本后由 PickupOnce 按当前状态写入，避免城镇误打补丁。
            WorkerThread.Start(config.IntervalMs, PickupOnce);
            CoreLog.Info("自动拾取 已开启");
        }

        internal static bool PickupRunning()
        {
            return WorkerThread.Running;
        }

        internal static void ShutdownPickup()
        {
            WorkerThread.Stop();
            ApplyAutoPick(false);
            AutoPickPatch.Restore();
            AutoPickPatch.Forget();
            Interlocked.Exchange(ref _patchReady, 0);
        }
    }
}
