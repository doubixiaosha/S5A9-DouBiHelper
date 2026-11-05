using System;
using System.Threading;
using DnfHelper.UI.Core.Base;
using DnfHelper.UI.Core.Shared;

namespace DnfHelper.UI.Core.Feature
{
    /// <summary>自动物品配置：Ids 为物品 ID 列表（逗号/空格分隔）。</summary>
    internal sealed class AutoItemConfig
    {
        internal bool Enabled;
        internal string Ids = string.Empty;

        internal AutoItemConfig Copy()
        {
            return new AutoItemConfig { Enabled = Enabled, Ids = Ids ?? string.Empty };
        }
    }

    /// <summary>
    /// 自动物品：调用游戏侧物品使用函数（人物对象虚表 +2296 取函数指针）。
    /// 参数是物品 **ID**（不是槽位、也不是名称）；
    /// 执行链为 [PlayerBase] → [人物对象] → +2296 取 CALL，压入物品 ID 后调用。
    /// 不做周期循环：仅在状态切入副本或 ALT+2 时单次触发。
    /// </summary>
    internal static class AutoItemFeature
    {
        private const int CallTimeoutMs = 3000;
        private const int CallGapMs = 50;

        private static readonly object ConfigLock = new object();
        private static AutoItemConfig _config = new AutoItemConfig();
        private static int _emptyWarned;
        private static int _shutdown;

        internal static void SetConfig(AutoItemConfig config)
        {
            AutoItemConfig snapshot;
            lock (ConfigLock)
            {
                _config = config == null ? new AutoItemConfig() : config.Copy();
                snapshot = _config;
            }
            Interlocked.Exchange(ref _emptyWarned, 0);
            Interlocked.Exchange(ref _shutdown, 0);

            CoreLog.Info(snapshot.Enabled ? "自动物品 已开启" : "自动物品 已关闭");
        }

        /// <summary>单次触发：自动触发与 ALT+2 共用，投递到线程池，不阻塞调用线程。</summary>
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
            // 与 BUFF 共用一条全局序列锁，整个物品序列期间独占。
            if (!CoreRuntime.TryEnterCallSequence("物品"))
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

                AutoItemConfig config;
                lock (ConfigLock)
                {
                    config = _config;
                }

                var ids = IdList.Parse(config.Ids);
                if (ids.Length == 0)
                {
                    if (Interlocked.Exchange(ref _emptyWarned, 1) == 0)
                    {
                        CoreLog.Warn("自动物品 列表为空，已忽略");
                    }
                    return;
                }
                if (GameReader.Player() == 0)
                {
                    CoreLog.Warn("自动物品 未进入角色，已忽略");
                    return;
                }

                var failed = false;
                foreach (var id in ids)
                {
                    if (!ItemCall(id))
                    {
                        failed = true;
                    }
                }
                if (failed)
                {
                    CoreLog.Warn("自动物品 执行失败");
                }
            }
            catch (Exception)
            {
                CoreLog.Error("自动物品 执行失败");
            }
            finally
            {
                CoreRuntime.ExitCallSequence();
            }
        }

        private static bool ItemCall(int itemId)
        {
            var code = new Asm();
            code.PushAd();
            code.MovEcxFromMem(GameAddresses.PlayerBase);
            code.MovEdxFromEcx();
            code.MovEaxEdx();
            code.AddEax(GameAddresses.ItemCallOffset);
            code.MovEbxFromEax();
            code.Push((uint)itemId);
            code.CallEbx();
            code.PopAd();

            string error;
            return RemoteExecutor.Instance.Execute(code, CallTimeoutMs, out error, CallGapMs);
        }
    }
}
