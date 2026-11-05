using System;
using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Feature
{
    /// <summary>BUFF 配置：Types 为 BUFF 类型编号列表（逗号/空格分隔）。</summary>
    internal sealed class BuffConfig
    {
        internal string Types = string.Empty;

        internal BuffConfig Copy()
        {
            return new BuffConfig { Types = Types ?? string.Empty };
        }
    }

    /// <summary>
    /// BUFF：调用游戏侧 BUFF 函数的调用链（三段函数 + 固定参数区）。
    /// 参数是游戏侧的“BUFF 类型编号”（如 1218），不是技能名或按键序列；
    /// 执行链为 BuffP1 → BuffP2 → BuffP1 → BuffP3 → BuffP1，参数压栈并复用 BuffMem。
    /// 不做周期循环，也不参与自动触发：只有 ALT+1 手动单次触发。
    /// </summary>
    internal static class BuffFeature
    {
        private const int CallTimeoutMs = 3000;
        private const int CallGapMs = 50;

        private static readonly object ConfigLock = new object();
        private static BuffConfig _config = new BuffConfig();
        private static int _emptyWarned;
        private static int _shutdown;

        internal static void SetConfig(BuffConfig config)
        {
            lock (ConfigLock)
            {
                _config = config == null ? new BuffConfig() : config.Copy();
            }
            Interlocked.Exchange(ref _emptyWarned, 0);
            Interlocked.Exchange(ref _shutdown, 0);
        }

        /// <summary>单次触发：自动触发与 ALT+1 共用，投递到线程池，不阻塞调用线程。</summary>
        internal static void TriggerOnce()
        {
            if (Interlocked.CompareExchange(ref _shutdown, 0, 0) != 0)
            {
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate { RunOnce(); });
        }

        internal static void Shutdown()
        {
            Interlocked.Exchange(ref _shutdown, 1);
        }

        private static void RunOnce()
        {
            if (Interlocked.CompareExchange(ref _shutdown, 0, 0) != 0)
            {
                return;
            }
            // 与物品共用一条全局序列锁，整个 BUFF 序列期间独占。
            if (!CoreRuntime.TryEnterCallSequence("BUFF"))
            {
                return;
            }
            try
            {
                if (!RemoteProcess.Instance.Attached)
                {
                    CoreLog.Warn("未附加游戏，已忽略");
                    return;
                }

                BuffConfig config;
                lock (ConfigLock)
                {
                    config = _config;
                }

                var types = IdList.Parse(config.Types);
                if (types.Length == 0)
                {
                    if (Interlocked.Exchange(ref _emptyWarned, 1) == 0)
                    {
                        CoreLog.Warn("BUFF 列表为空，已忽略");
                    }
                    return;
                }
                if (GameReader.Player() == 0)
                {
                    CoreLog.Warn("BUFF 未进入角色，已忽略");
                    return;
                }

                var failed = false;
                foreach (var type in types)
                {
                    if (!BuffCall(type))
                    {
                        failed = true;
                    }
                }
                if (failed)
                {
                    CoreLog.Warn("BUFF 执行失败");
                }
            }
            catch (Exception)
            {
                CoreLog.Error("BUFF 执行失败");
            }
            finally
            {
                CoreRuntime.ExitCallSequence();
            }
        }

        private static bool BuffCall(int buffType)
        {
            var code = new Asm();
            code.PushAd();
            code.Push((uint)GameAddresses.BuffMem);
            code.Push((uint)buffType);
            code.MovEax(GameAddresses.BuffP1);
            code.CallEax();
            code.MovEcxEax();
            code.MovEbx(GameAddresses.BuffP2);
            code.CallEbx();
            code.MovEsiEax();
            code.Push((uint)buffType);
            code.MovEcx(GameAddresses.BuffP1);
            code.CallEcx();
            code.MovEcxEax();
            code.MovEdx(GameAddresses.BuffP3);
            code.CallEdx();
            code.MovEsi(GameAddresses.BuffP1);
            code.CallEsi();
            code.PopAd();

            string error;
            return RemoteExecutor.Instance.Execute(code, CallTimeoutMs, out error, CallGapMs);
        }
    }
}
