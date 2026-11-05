using DnfHelper.UI.Core.Base;

namespace DnfHelper.UI.Core.Shared
{
    /// <summary>
    /// 坐标瞬移与强制过图 CALL（均在目标进程内远程执行）。
    /// </summary>
    internal static class Movement
    {
        internal static bool CoordTeleport(uint player, int x, int y, int z)
        {
            if (player == 0)
            {
                return false;
            }

            var code = new Asm();
            code.Push((uint)z);
            code.Push((uint)y);
            code.Push((uint)x);
            code.MovEcx(player);
            code.MovEax(GameAddresses.CallCoordMove);
            code.CallEax();

            string error;
            return RemoteExecutor.Instance.Execute(code, 3000, out error);
        }

        internal static bool ForceMove(int dir, int cellX, int cellY, bool force)
        {
            if (dir < 0 || dir > 3 || cellX < 0 || cellY < 0)
            {
                return false;
            }

            var room = RemoteProcess.Instance.ReadDword(GameAddresses.RoomBase);
            if (room == 0)
            {
                return false;
            }
            var context = RemoteProcess.Instance.ReadDword(room + GameAddresses.OffRoomTime);
            if (context == 0 || !RemoteProcess.Instance.IsReadable(context, 0x190))
            {
                return false;
            }

            var code = new Asm();
            code.Push(force ? 1u : 0u); // p4 强制标志
            code.Push((uint)cellY);
            code.Push((uint)cellX);
            code.Push((uint)dir);
            code.MovEcx(context);
            code.MovEax(GameAddresses.CallForceRoomMove);
            code.CallEax();

            string error;
            return RemoteExecutor.Instance.Execute(code, 3000, out error);
        }
    }
}
