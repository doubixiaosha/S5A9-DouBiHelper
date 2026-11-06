using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Feature
{
    /// <summary>
    /// 服务端「魔界冒险助手」文本通道。
    ///
    /// CMD17 正文固定 11 字节头：u8 类型(3) / u16 层(4) / u32 参数(0) / u32 文本长度 / ASCII 文本；
    /// 总正文不超过 107 字节。CMD870 能力探测正文为三段 ASCII：95_HELPER_CAPS/2 nonce charId，不超过 90 字节。
    /// 正文逐字节交给游戏自身的 1 字节写入函数，再调用发送函数，不改写任何地址。
    ///
    /// nonce 每次请求重新生成：32 字符小写十六进制，非全 0。
    /// 与 BUFF / 物品共用全局调用序列锁；同一时刻只允许一条助手序列。
    /// 发送成功只代表包已交给服务端，是否生效由服务端条件决定。
    /// </summary>
    internal static class HelperFeature
    {
        private const int MaxChatBody = 107;
        private const int MaxProbeBody = 90;

        private static readonly object StateLock = new object();
        private static readonly RNGCryptoServiceProvider RandomSource = new RNGCryptoServiceProvider();
        private static readonly string ZeroNonce = new string('0', 32);

        private static bool _wantPickup;
        private static bool _wantHell;
        private static int _sentPickup = -1;
        private static int _sentHell = -1;
        private static int _probed;

        /// <summary>断开/退出时复位：下次附加重新探测并按配置重下。</summary>
        internal static void Shutdown()
        {
            ResetProgress();
        }

        /// <summary>配置下发：只记录目标状态；仅在已附加且与上次下发不同时发包。</summary>
        internal static void SetConfig(bool pickup, bool hell)
        {
            lock (StateLock)
            {
                _wantPickup = pickup;
                _wantHell = hell;
            }
            Sync();
        }

        /// <summary>已附加时比较目标状态与已下发状态，有差异才排入后台序列。</summary>
        private static void Sync()
        {
            if (!RemoteProcess.Instance.Attached)
            {
                return;
            }

            HelperBatch batch;
            lock (StateLock)
            {
                var sendPickup = false;
                var pickupOn = false;
                var sendHell = false;
                var hellOn = false;

                if (_wantPickup != (_sentPickup > 0))
                {
                    sendPickup = true;
                    pickupOn = _wantPickup;
                    _sentPickup = _wantPickup ? 1 : 0;
                }
                if (_wantHell != (_sentHell > 0))
                {
                    sendHell = true;
                    hellOn = _wantHell;
                    _sentHell = _wantHell ? 1 : 0;
                }
                if (!sendPickup && !sendHell)
                {
                    return;
                }

                // 开启类指令前登记直入包能力；同一次序列只探测一次。
                var probe = (pickupOn || hellOn) && _probed == 0;
                if (probe)
                {
                    _probed = 1;
                }

                batch = new HelperBatch
                {
                    Probe = probe,
                    SendPickup = sendPickup,
                    PickupOn = pickupOn,
                    SendHell = sendHell,
                    HellOn = hellOn,
                };
            }

            ThreadPool.QueueUserWorkItem(delegate { Run(batch); });
        }

        private static void Run(HelperBatch batch)
        {
            try
            {
                if (!RemoteProcess.Instance.Attached)
                {
                    ResetProgress();
                    CoreLog.Warn("助手 未附加");
                    return;
                }
                if (!CoreRuntime.TryEnterCallSequence("助手"))
                {
                    ResetProgress();
                    return;
                }
                try
                {
                    var characterId = GameReader.CharacterId();
                    if (characterId == 0)
                    {
                        ResetProgress();
                        CoreLog.Warn("助手 已忽略");
                        return;
                    }

                    if (batch.Probe && !SendCapsProbe(characterId))
                    {
                        ResetProgress();
                        CoreLog.Warn("助手 发送失败");
                        return;
                    }
                    if (batch.SendPickup && !SendCommand(characterId, "pickup", batch.PickupOn ? 1 : 0))
                    {
                        ResetProgress();
                        CoreLog.Warn("助手 发送失败");
                        return;
                    }
                    if (batch.SendHell && !SendCommand(characterId, "hell", batch.HellOn ? 1 : 0))
                    {
                        ResetProgress();
                        CoreLog.Warn("助手 发送失败");
                        return;
                    }

                    CoreLog.Info("助手 已发送");
                }
                finally
                {
                    CoreRuntime.ExitCallSequence();
                }
            }
            catch (Exception)
            {
                ResetProgress();
                CoreLog.Warn("助手 发送失败");
            }
        }

        private static bool SendCapsProbe(ushort characterId)
        {
            var text = "95_HELPER_CAPS/2 " + NewNonce() + " " +
                       characterId.ToString(CultureInfo.InvariantCulture);
            var body = Encoding.ASCII.GetBytes(text);
            if (body.Length > MaxProbeBody)
            {
                return false;
            }
            return PacketSender.SendRaw(GameAddresses.CmdHelperSync, body);
        }

        private static bool SendCommand(ushort characterId, string op, int arg)
        {
            var text = "#95-helper/1 " + NewNonce() + " " +
                       characterId.ToString(CultureInfo.InvariantCulture) + " " + op + " " +
                       arg.ToString(CultureInfo.InvariantCulture);
            var textBytes = Encoding.ASCII.GetBytes(text);

            // 11 字节头 + 文本；层固定 4、参数固定 0、文本长度只算 ASCII 文本。
            var body = new byte[11 + textBytes.Length];
            body[0] = 3;
            body[1] = 4;
            body[2] = 0;
            WriteDword(body, 7, (uint)textBytes.Length);
            Buffer.BlockCopy(textBytes, 0, body, 11, textBytes.Length);

            if (body.Length > MaxChatBody)
            {
                return false;
            }
            return PacketSender.SendRaw(GameAddresses.CmdChat, body);
        }

        /// <summary>32 字符小写十六进制；全 0 时重新生成。</summary>
        private static string NewNonce()
        {
            var buffer = new byte[16];
            RandomSource.GetBytes(buffer);
            var chars = new char[32];
            for (var i = 0; i < buffer.Length; i++)
            {
                chars[i * 2] = HexChar(buffer[i] >> 4);
                chars[i * 2 + 1] = HexChar(buffer[i] & 0x0F);
            }
            var text = new string(chars);
            return text == ZeroNonce ? NewNonce() : text;
        }

        private static char HexChar(int value)
        {
            return value < 10 ? (char)('0' + value) : (char)('a' + value - 10);
        }

        private static void WriteDword(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void ResetProgress()
        {
            lock (StateLock)
            {
                _sentPickup = -1;
                _sentHell = -1;
                _probed = 0;
            }
        }

        private sealed class HelperBatch
        {
            internal bool Probe;
            internal bool SendPickup;
            internal bool PickupOn;
            internal bool SendHell;
            internal bool HellOn;
        }
    }
}
