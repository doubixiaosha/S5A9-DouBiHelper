using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Feature
{
    internal sealed class TraverseConfig
    {
        internal bool Enabled;
        internal int Mode = 1; // 1=组包 2=强制 3=瞬移 4=移动（占位）

        internal TraverseConfig Copy()
        {
            return new TraverseConfig { Enabled = Enabled, Mode = Mode };
        }
    }

    /// <summary>
    /// ALT+方向键触发一次顺图。
    /// </summary>
    internal static class TraverseFeature
    {
        private static readonly object ConfigLock = new object();
        private static readonly Worker WorkerThread = new Worker();
        private static TraverseConfig _config = new TraverseConfig();
        private static int _pendingDir = -1;

        private static TraverseConfig Snapshot()
        {
            lock (ConfigLock)
            {
                return _config.Copy();
            }
        }

        private static bool TeleportThrough(int dir, Rect rect)
        {
            float outX;
            float outY;
            switch (dir)
            {
                case 0:
                    outX = rect.X + rect.Width + 20.0f;
                    outY = rect.Y + rect.Height / 2.0f;
                    break;
                case 1:
                    outX = rect.X - 20.0f;
                    outY = rect.Y + rect.Height / 2.0f;
                    break;
                case 2:
                    outX = rect.X + rect.Width / 2.0f;
                    outY = rect.Y + rect.Height + 20.0f;
                    break;
                case 3:
                default:
                    outX = rect.X + rect.Width / 2.0f;
                    outY = rect.Y - 20.0f;
                    break;
            }
            var midX = rect.X + rect.Width / 2.0f;
            var midY = rect.Y;

            var player = GameReader.Player();
            if (!Movement.CoordTeleport(player, (int)outX, (int)outY, 0))
            {
                return false;
            }
            // 等游戏处理门外落点，再回到房间中点；该等待只发生在功能线程。
            Thread.Sleep(200);
            return Movement.CoordTeleport(player, (int)midX, (int)midY, 0);
        }

        private static bool ExecuteStep(TraverseConfig config, int dir)
        {
            if (GameReader.Map() == 0)
            {
                CoreLog.Warn("顺图未执行");
                return false;
            }

            switch (config.Mode)
            {
                case 1:
                {
                    // 组包顺图：CMD45
                    RoomCoord current;
                    if (!GameReader.ReadCurRoom(out current))
                    {
                        CoreLog.Warn("顺图未执行");
                        return false;
                    }
                    RoomCoord next;
                    if (!GameReader.CanStep(current, dir, out next))
                    {
                        CoreLog.Warn("顺图未执行");
                        return false;
                    }
                    var okay = PacketSender.MoveRoom(next.X, next.Y);
                    CoreLog.Info(okay ? "ALT+方向键 已顺图" : "顺图未执行");
                    return okay;
                }
                case 2:
                {
                    // 强制顺图
                    RoomCoord current;
                    if (!GameReader.ReadCurRoom(out current))
                    {
                        CoreLog.Warn("顺图未执行");
                        return false;
                    }
                    var okay = Movement.ForceMove(dir, current.X, current.Y, true);
                    CoreLog.Info(okay ? "ALT+方向键 已顺图" : "顺图未执行");
                    return okay;
                }
                case 3:
                {
                    // 瞬移顺图
                    if (!GameReader.ReadDoorOpen())
                    {
                        CoreLog.Warn("顺图未执行");
                        return false;
                    }
                    Rect rect;
                    if (!GameReader.ReadRoomRect(dir, out rect))
                    {
                        CoreLog.Warn("顺图未执行");
                        return false;
                    }
                    var okay = TeleportThrough(dir, rect);
                    CoreLog.Info(okay ? "ALT+方向键 已顺图" : "顺图未执行");
                    return okay;
                }
                case 4:
                    // 占位：设计文档要求保留“移动顺图”入口，本轮不接底层逻辑。
                    // TODO: 后续确定移动方式（原生过图 CALL / 移动包）后在此实现。
                    CoreLog.Warn("顺图未执行");
                    return false;
                default:
                    CoreLog.Warn("顺图未执行");
                    return false;
            }
        }

        private static void TraverseLoop()
        {
            var dir = Interlocked.Exchange(ref _pendingDir, -1);
            if (dir < 0 || dir > 3)
            {
                return;
            }
            var config = Snapshot();
            if (!config.Enabled)
            {
                CoreLog.Warn("顺图 未开启，已忽略");
                return;
            }
            if (!RemoteProcess.Instance.Attached)
            {
                CoreLog.Warn("未附加游戏，已忽略");
                return;
            }
            ExecuteStep(config, dir);
        }

        internal static void SetTraverseConfig(TraverseConfig config)
        {
            lock (ConfigLock)
            {
                _config = config ?? new TraverseConfig();
            }
            Interlocked.Exchange(ref _pendingDir, -1);
            if (config == null || !config.Enabled)
            {
                WorkerThread.Stop();
                CoreLog.Info("顺图 已关闭");
                return;
            }
            if (!RemoteProcess.Instance.Attached)
            {
                CoreLog.Warn("未附加游戏，已忽略");
                return;
            }
            WorkerThread.Start(25, TraverseLoop);
            CoreLog.Info("顺图 已开启");
        }

        internal static void TraverseStep(int dir)
        {
            if (dir < 0 || dir > 3)
            {
                return;
            }
            Interlocked.Exchange(ref _pendingDir, dir);
        }

        internal static bool TraverseRunning()
        {
            return WorkerThread.Running;
        }

        internal static void ShutdownTraverse()
        {
            WorkerThread.Stop();
        }
    }
}
