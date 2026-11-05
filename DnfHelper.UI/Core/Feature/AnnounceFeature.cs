using System;
using System.Text;
using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Feature
{
    /// <summary>
    /// 游戏内彩色公告。
    ///
    /// 机制：不打包、不发聊天/喇叭包，而是在游戏进程里直接 CALL 客户端的公告函数
    /// 0x02E2C9E0，由客户端弹出带颜色的公告。
    /// 调用约定为 thiscall：
    ///   ecx = [[0x062EF53C] + 0x54]     ; this（商店基址槽 -> 商店对象 +0x54）
    ///   栈参 = buf - 4                   ; buf 为 uint32[50]，游戏从 +4 开始读
    ///
    /// 结构体字段（对 buf 的下标）：
    ///   buf[0] 文本指针（UTF-16LE，调用期间必须存活）
    ///   buf[4] 颜色，COLORREF（0x00BBGGRR，BGR 序）
    ///   buf[5] 类型，1->18 / 2->37 / 3->55，其余回落 37
    ///   buf[9] 图标路径指针（置 0，不传图标）
    ///
    /// 本进程在游戏外部运行，缓冲不能放栈上：文本与结构体两块缓冲都在目标进程
    /// VirtualAllocEx 申请并长期持有，注入 stub 执行 CALL，断开时释放。
    ///
    /// 前缀 [DouBi] ；500ms 节流；未附加/未进入角色静默跳过；
    /// 失败只告警一次，不抛异常、不刷屏。
    /// </summary>
    internal static class AnnounceFeature
    {
        /// <summary>默认前缀。</summary>
        internal const string Prefix = "[DouBi] ";

        private const uint TextBufSize = 0x400;   // 200 个 wchar，够放前缀 + 文本 + 结尾 0
        private const uint StructBufSize = 4 + 50 * 4; // 游戏从 ptr+4 起读，故多留 4 字节头
        private const uint CallTimeoutMs = 5000;
        private const int ThrottleMs = 500;

        // 类型档位：缺省 type=2 对应 37。
        private const int TypeDefault = 37;
        // COLORREF 0x00BBGGRR：缺省绿色。
        private const int ColorGreen = 0x0000FF00;

        private static readonly object SendLock = new object();
        private static uint _textBuf;
        private static uint _structBuf;
        private static uint _bufPid;
        private static int _lastSendTick;
        private static int _warned;

        /// <summary>异步发送一条公告；调用线程（UI 线程）不会被远程执行阻塞。</summary>
        internal static void Announce(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            var payload = Prefix + text;
            ThreadPool.QueueUserWorkItem(delegate { Send(payload); });
        }

        /// <summary>释放远程缓冲（断开/退出时调用）。</summary>
        internal static void Shutdown()
        {
            lock (SendLock)
            {
                ReleaseBuffers(RemoteProcess.Instance);
                _bufPid = 0;
            }
            Interlocked.Exchange(ref _warned, 0);
            Interlocked.Exchange(ref _lastSendTick, 0);
        }

        private static void Send(string text)
        {
            try
            {
                lock (SendLock)
                {
                    var process = RemoteProcess.Instance;
                    if (!process.Attached)
                    {
                        return;
                    }
                    if (GameReader.Player() == 0)
                    {
                        return;
                    }
                    if (!EnsureBuffers(process))
                    {
                        return;
                    }

                    // 文本整块覆盖（含 UTF-16 结尾 0），前缀已拼好。
                    var chars = Encoding.Unicode.GetBytes(text + "\0");
                    if (chars.Length > TextBufSize)
                    {
                        return;
                    }
                    var textPayload = new byte[TextBufSize];
                    Array.Copy(chars, textPayload, chars.Length);
                    if (!process.WriteBytes(_textBuf, textPayload, (int)TextBufSize))
                    {
                        WarnOnce("公告文本写入失败");
                        return;
                    }

                    // 节流：与上一条至少间隔 500ms，缓冲已写好，等待不丢内容。
                    var elapsed = Environment.TickCount - _lastSendTick;
                    if (_lastSendTick != 0 && elapsed >= 0 && elapsed < ThrottleMs)
                    {
                        Thread.Sleep(ThrottleMs - elapsed);
                    }
                    _lastSendTick = Environment.TickCount;

                    // this 指针先本地校验，避免空指针在远程线程里把游戏带崩。
                    var shop = process.ReadDword(GameAddresses.AnnounceShopBase);
                    if (shop == 0 || !process.IsReadable(shop + (uint)GameAddresses.AnnounceThisOffset, 4))
                    {
                        return;
                    }
                    var self = process.ReadDword(shop + (uint)GameAddresses.AnnounceThisOffset);
                    if (self == 0)
                    {
                        return;
                    }

                    var code = new Asm();
                    code.PushAd();
                    code.MovEcx(GameAddresses.AnnounceShopBase);
                    code.MovEaxFromEcx();
                    code.MovEcxFromEaxDisp8((byte)GameAddresses.AnnounceThisOffset);
                    code.MovEax(_structBuf);
                    code.PushEax();
                    code.MovEdx(GameAddresses.AnnounceCall);
                    code.CallEdx();
                    code.PopAd();

                    string error;
                    if (!RemoteExecutor.Instance.Execute(code, CallTimeoutMs, out error))
                    {
                        WarnOnce("公告发送失败");
                    }
                }
            }
            catch (Exception)
            {
                WarnOnce("公告发送失败");
            }
        }

        private static bool EnsureBuffers(RemoteProcess process)
        {
            if (_bufPid == process.Pid && _textBuf != 0 && _structBuf != 0 &&
                process.IsReadable(_textBuf, 4))
            {
                return true;
            }

            ReleaseBuffers(process);
            _bufPid = process.Pid;

            _textBuf = process.AllocateRemote(TextBufSize, NativeApi.PageReadWrite);
            _structBuf = process.AllocateRemote(StructBufSize, NativeApi.PageReadWrite);
            if (_textBuf == 0 || _structBuf == 0)
            {
                ReleaseBuffers(process);
                return false;
            }

            // 结构体：buf[0] 文本 / buf[4] 颜色 / buf[5] 类型（游戏从 ptr+4 起读）。
            // buf[9]（+0x28）图标指针留 0：不再传图标。
            var st = new byte[StructBufSize];
            WriteDword(st, 4, _textBuf);
            WriteDword(st, 0x14, (uint)ColorGreen);
            WriteDword(st, 0x18, (uint)TypeDefault);
            WriteDword(st, 0x28, 0);
            if (!process.WriteBytes(_structBuf, st, (int)StructBufSize))
            {
                ReleaseBuffers(process);
                return false;
            }
            return true;
        }

        private static void ReleaseBuffers(RemoteProcess process)
        {
            if (_textBuf != 0)
            {
                process.FreeRemote(_textBuf);
            }
            if (_structBuf != 0)
            {
                process.FreeRemote(_structBuf);
            }
            _textBuf = 0;
            _structBuf = 0;
        }

        private static void WriteDword(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>失败只提示一次，避免高频事件刷屏。</summary>
        private static void WarnOnce(string message)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                CoreLog.Warn(message);
            }
        }
    }
}
