using System.Collections.Generic;
using DnfHelper.UI.Core.Base;

namespace DnfHelper.UI.Core.Shared
{
    internal struct Field
    {
        internal int Value;
        internal int Size;

        internal Field(int value, int size)
        {
            Value = value;
            Size = size;
        }
    }

    /// <summary>
    /// BeginPacket → Encrypt → Send 的单次远程执行。
    /// 每条 CMD 的字段顺序与宽度按下表定义，改动前先确认服务端解析顺序。
    /// </summary>
    internal static class PacketSender
    {
        private const uint SendTimeoutMs = 3000;

        private static uint EncryptAddress(int size)
        {
            switch (size)
            {
                case 1: return GameAddresses.CallEncrypt;
                case 2: return GameAddresses.CallEncrypt + 48;
                case 4: return GameAddresses.CallEncrypt + 96;
                default: return 0;
            }
        }

        internal static bool Send(int head, params Field[] fields)
        {
            if (fields != null)
            {
                foreach (var field in fields)
                {
                    if (field.Size != 1 && field.Size != 2 && field.Size != 4)
                    {
                        return false;
                    }
                }
            }

            var sendThis = RemoteProcess.Instance.ReadDword(GameAddresses.SendBase);
            if (sendThis == 0)
            {
                return false;
            }

            var code = new Asm();
            // BeginPacket(head, 0)：this 由 ecx 传入。
            code.Push(0);
            code.Push((uint)head);
            code.MovEcx(sendThis);
            code.MovEax(GameAddresses.CallBuffer);
            code.CallEax();

            if (fields != null)
            {
                foreach (var field in fields)
                {
                    code.Push((uint)field.Value);
                    code.MovEcx(sendThis);
                    code.MovEax(EncryptAddress(field.Size));
                    code.CallEax();
                }
            }

            code.MovEcx(sendThis);
            code.MovEax(GameAddresses.CallSend);
            code.CallEax();

            string error;
            return RemoteExecutor.Instance.Execute(code, SendTimeoutMs, out error);
        }

        /// <summary>
        /// 明文正文发包：BeginPacket → 逐字节调用游戏的 1 字节写入函数 → Send。
        /// 游戏的「写入」函数本身就是明文追加，服务端按正文原样解析，因此文本可逐字节送入。
        /// </summary>
        internal static bool SendRaw(int head, byte[] body)
        {
            if (body == null || body.Length == 0)
            {
                return false;
            }

            var sendThis = RemoteProcess.Instance.ReadDword(GameAddresses.SendBase);
            if (sendThis == 0)
            {
                return false;
            }

            var code = new Asm();
            code.Push(0);
            code.Push((uint)head);
            code.MovEcx(sendThis);
            code.MovEax(GameAddresses.CallBuffer);
            code.CallEax();

            foreach (var value in body)
            {
                code.Push(value);
                code.MovEcx(sendThis);
                code.MovEax(EncryptAddress(1));
                code.CallEax();
            }

            code.MovEcx(sendThis);
            code.MovEax(GameAddresses.CallSend);
            code.CallEax();

            string error;
            return RemoteExecutor.Instance.Execute(code, SendTimeoutMs, out error);
        }

        internal static bool PickupItemAt(int groundKey, int px, int py, int ix, int iy)
        {
            return Send(GameAddresses.CmdPickup,
                new Field(groundKey, 4),
                new Field(0, 1), // 硬校 body[4]=0
                new Field(1, 1), // Context=1
                new Field(px, 2),
                new Field(py, 2),
                new Field(0, 2),
                new Field(ix, 2),
                new Field(iy, 2),
                new Field(0, 2),
                new Field(0, 2));
        }

        internal static bool SellItem(int slot)
        {
            const int count = 1; // 装备不可叠放
            const int list = 0; // 人物背包
            const int npcContext = 2;
            const int windowState = 33;
            var proto = slot + GameAddresses.EquipSlotProto;
            return Send(GameAddresses.CmdSellItem,
                new Field(list, 1),
                new Field(proto, 2),
                new Field(count, 4),
                new Field(npcContext, 4),
                new Field(windowState, 4),
                new Field(2 * (proto + count + list) + 1, 4));
        }

        internal static bool Disassemble(int slot)
        {
            var proto = slot + GameAddresses.EquipSlotProto;
            return Send(GameAddresses.CmdDisassemble,
                new Field(proto, 2),
                new Field(0, 1),
                new Field(65535, 2),
                new Field(319, 4));
        }

        internal static bool OrganizeBag()
        {
            return Send(GameAddresses.CmdOrganizeBag,
                new Field(6, 4),
                new Field(16, 1),
                new Field(0, 1),
                new Field(24, 1),
                new Field(1, 1),
                new Field(32, 1),
                new Field(0, 1));
        }

        internal static bool MoveRoom(int targetX, int targetY)
        {
            var fields = new List<Field>(100);
            fields.Add(new Field(targetX, 1));
            fields.Add(new Field(targetY, 1));
            for (var i = 2; i < 100; i++)
            {
                fields.Add(new Field(0, 1));
            }
            return Send(GameAddresses.CmdMoveRoom, fields.ToArray());
        }
    }
}
