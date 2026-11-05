using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// 超级解密/加密：读取目标进程的密钥表后自行换算。
    /// 8 字节槽位，[address] = enc_id、[address+4] = cipher（4 字节值），密钥表挂在 DecryptBase 上。
    /// 门状态、掉落 key、物品数量、地图索引、BOSS 房间坐标都必须用此算法。
    /// </summary>
    internal static class Codec
    {
        internal static int SuperDecrypt(uint address)
        {
            var process = RemoteProcess.Instance;
            if (!process.IsReadable(address, 8))
            {
                return -1;
            }
            var encId = process.ReadDword(address);
            if (encId == 0xFFFFFFFFu)
            {
                return -1;
            }
            var cipher = process.ReadDword(address + 4);
            if (cipher == 0xFFFFFFFFu)
            {
                return -1;
            }
            var table = process.ReadDword(GameAddresses.DecryptBase);
            if (table == 0 || table == 0xFFFFFFFFu)
            {
                return -1;
            }
            var node = process.ReadDword(table + ((encId >> 16) * 4) + 36);
            if (node == 0 || node == 0xFFFFFFFFu)
            {
                return -1;
            }
            var key = process.ReadDword(node + ((encId & 0xFFFF) * 4) + 8468);
            if (key == 0xFFFFFFFFu)
            {
                return -1;
            }
            var k16 = key & 0xFFFF;
            var mask = (k16 << 16) | k16;
            return unchecked((int)(mask ^ cipher));
        }

        internal static bool SuperEncrypt(uint address, uint value, int byteSize)
        {
            var process = RemoteProcess.Instance;
            var key = (ushort)(process.ReadDword(GameAddresses.DecryptBase) & 0xFFFF);
            var mask = ((uint)key << 16) | key;
            var cipher = value ^ mask;

            switch (byteSize)
            {
                case 1:
                {
                    var buffer = new[] { (byte)(cipher & 0xFF) };
                    return process.WriteBytes(address, buffer, buffer.Length);
                }
                case 2:
                {
                    var word = (ushort)(cipher & 0xFFFF);
                    var buffer = new[]
                    {
                        (byte)(word & 0xFF),
                        (byte)((word >> 8) & 0xFF),
                    };
                    return process.WriteBytes(address, buffer, buffer.Length);
                }
                case 4:
                    return process.WriteDword(address, cipher);
                default:
                    return false;
            }
        }
    }
}
