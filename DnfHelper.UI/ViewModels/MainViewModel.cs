using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DnfHelper.UI.Models;
using DnfHelper.UI.Services;

namespace DnfHelper.UI.ViewModels
{
    /// <summary>
    /// 主界面状态与指令下发。持续循环全部在进程内 C# 运行时线程中，这里只做低频开关、状态同步与配置持久化。
    /// </summary>
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private const string TargetExeName = "ARAD.EXE";

        /// <summary>自动触发的统一延迟（毫秒）：进入副本后的自动物品、副本→城镇后的装备处理共用。</summary>
        private const int AutoTriggerDelayMs = 1500;

        /// <summary>助手开关的下发间隔（毫秒）：与聊天共用 1 秒 5 条限流，界面侧至少间隔 1 秒。</summary>
        private const int HelperApplyGapMs = 1000;

        private readonly DispatcherTimer _statusTimer;

        private bool _isAttached;
        private bool _isLightTheme;
        private string _statusText = "未附加";
        private string _characterIdReadText = "读取 —";
        private int _autoCharacterId;
        private string _characterLine = "未附加";
        private string _locationLine = string.Empty;
        private string _roomLine = string.Empty;
        private bool _hasLocationLine;
        private bool _hasRoomLine;
        private uint _attachedPid;
        private int _lastGameState = int.MinValue;
        private readonly object _delayLock = new object();
        private Timer _enterDelayTimer;
        private int _enterPending;
        private Timer _exitDelayTimer;
        private int _exitPending;
        private int _lastHelperApplyTick;
        private Timer _helperDelayTimer;
        private bool _startupAnnounced;
        private bool _disposed;

        public MainViewModel(AppConfig config)
        {
            Config = config ?? new AppConfig();
            _isLightTheme = Config.Theme == "Light";
            VersionText = "v" + ProgramVersion();

            ReconnectCommand = new RelayCommand(() => Connect());
            ToggleThemeCommand = new RelayCommand(ToggleTheme);

            Equip = new EquipOptionsViewModel(Config, SaveAndApply);

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += OnStatusTick;
            _statusTimer.Start();
        }

        public AppConfig Config { get; }

        public EquipOptionsViewModel Equip { get; }

        public string[] PickupModes { get; } = { "吸物拾取", "自动入包" };
        public string[] TraverseModes { get; } = { "组包顺图", "强制顺图", "瞬移顺图", "移动顺图（占位）" };

        public ICommand ReconnectCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        public bool IsAttached
        {
            get => _isAttached;
            private set
            {
                if (_isAttached == value)
                {
                    return;
                }
                _isAttached = value;
                OnPropertyChanged();
            }
        }

        /// <summary>底部状态栏左侧：附加状态 / 未附加原因 / 已附加时的游戏状态。</summary>
        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value)
                {
                    return;
                }
                _statusText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>底部状态栏右侧：程序集版本号。</summary>
        public string VersionText { get; }

        /// <summary>角色信息第一行：名称 / 等级 / 职业 / 当前状态。</summary>
        public string CharacterLine
        {
            get => _characterLine;
            private set => SetField(ref _characterLine, value);
        }

        /// <summary>角色信息第二行：城镇ID/区域ID/坐标 或 副本ID/副本名称。</summary>
        public string LocationLine
        {
            get => _locationLine;
            private set => SetField(ref _locationLine, value);
        }

        /// <summary>副本地图中的房间信息行（仅副本内可见）。</summary>
        public string RoomLine
        {
            get => _roomLine;
            private set => SetField(ref _roomLine, value);
        }

        public bool HasLocationLine
        {
            get => _hasLocationLine;
            private set => SetField(ref _hasLocationLine, value);
        }

        public bool HasRoomLine
        {
            get => _hasRoomLine;
            private set => SetField(ref _hasRoomLine, value);
        }

        private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            if (Equals(field, value))
            {
                return;
            }
            field = value;
            OnPropertyChanged(name);
        }

        public bool IsLightTheme
        {
            get => _isLightTheme;
            set
            {
                if (_isLightTheme == value)
                {
                    return;
                }
                _isLightTheme = value;
                Config.Theme = value ? "Light" : "Dark";
                ThemeService.Apply(Config.Theme);
                SaveConfig();
                OnPropertyChanged();
            }
        }

        public bool PickupExpanded
        {
            get => Config.Ui.PickupExpanded;
            set => SetExpanded(value, Config.Ui.PickupExpanded, v => Config.Ui.PickupExpanded = v, nameof(PickupExpanded));
        }

        public bool EquipExpanded
        {
            get => Config.Ui.EquipExpanded;
            set
            {
                if (!SetExpanded(value, Config.Ui.EquipExpanded, v => Config.Ui.EquipExpanded = v, nameof(EquipExpanded)))
                {
                    return;
                }
                if (value)
                {
                    Equip.Refresh();
                }
            }
        }

        public bool PickupEnabled
        {
            get => Config.Pickup.Enabled;
            set
            {
                if (Config.Pickup.Enabled == value)
                {
                    return;
                }
                Config.Pickup.Enabled = value;
                OnPropertyChanged();
                ApplyPickup();
                SaveConfig();
                CoreBridge.Announce(value ? "自动拾取 已开启" : "自动拾取 已关闭");
            }
        }

        public int PickupModeIndex
        {
            get => (int)Config.Pickup.Mode - 1;
            set
            {
                var mode = value == 1 ? PickupMode.AutoSend : PickupMode.Absorb;
                if (Config.Pickup.Mode == mode)
                {
                    return;
                }
                Config.Pickup.Mode = mode;
                OnPropertyChanged();
                ApplyPickup();
                SaveConfig();
            }
        }

        public bool EquipEnabled
        {
            get => Config.Equip.Enabled;
            set
            {
                if (Config.Equip.Enabled == value)
                {
                    return;
                }
                Config.Equip.Enabled = value;
                OnPropertyChanged();
                ApplyEquip();
                SaveConfig();
            }
        }

        public bool TraverseEnabled
        {
            get => Config.Traverse.Enabled;
            set
            {
                if (Config.Traverse.Enabled == value)
                {
                    return;
                }
                Config.Traverse.Enabled = value;
                OnPropertyChanged();
                ApplyTraverse();
                SaveConfig();
            }
        }

        public int TraverseModeIndex
        {
            get => (int)Config.Traverse.Mode - 1;
            set
            {
                var mode = (TraverseMode)(value + 1);
                if (Config.Traverse.Mode == mode)
                {
                    return;
                }
                Config.Traverse.Mode = mode;
                OnPropertyChanged();
                ApplyTraverse();
                SaveConfig();
            }
        }

        public string BuffTypes
        {
            get => Config.Buff.Types;
            set
            {
                var text = value ?? string.Empty;
                if (Config.Buff.Types == text)
                {
                    return;
                }
                Config.Buff.Types = text;
                OnPropertyChanged();
                ApplyBuff();
                SaveConfig();
            }
        }

        public bool AutoItemEnabled
        {
            get => Config.AutoItem.Enabled;
            set
            {
                if (Config.AutoItem.Enabled == value)
                {
                    return;
                }
                Config.AutoItem.Enabled = value;
                OnPropertyChanged();
                ApplyAutoItem();
                SaveConfig();
            }
        }

        public string AutoItemIds
        {
            get => Config.AutoItem.Ids;
            set
            {
                var text = value ?? string.Empty;
                if (Config.AutoItem.Ids == text)
                {
                    return;
                }
                Config.AutoItem.Ids = text;
                OnPropertyChanged();
                ApplyAutoItem();
                SaveConfig();
            }
        }

        /// <summary>助手「自动直入包」：打开时探测 + 开启，关闭时下达关闭。</summary>
        public bool HelperPickupEnabled
        {
            get => Config.Helper.PickupEnabled;
            set
            {
                if (Config.Helper.PickupEnabled == value)
                {
                    return;
                }
                Config.Helper.PickupEnabled = value;
                OnPropertyChanged();
                SaveConfig();
                RequestHelperApply();
            }
        }

        /// <summary>助手「深渊连刷」：打开时探测 + 开启，关闭时下达关闭。</summary>
        public bool HelperHellEnabled
        {
            get => Config.Helper.HellEnabled;
            set
            {
                if (Config.Helper.HellEnabled == value)
                {
                    return;
                }
                Config.Helper.HellEnabled = value;
                OnPropertyChanged();
                SaveConfig();
                RequestHelperApply();
            }
        }

        /// <summary>角色ID 只读回显：自动读到的值；未进入角色/读取失败显示「—」。</summary>
        public string CharacterIdReadText
        {
            get => _characterIdReadText;
            private set => SetField(ref _characterIdReadText, value);
        }

        public void InitializeCore()
        {
            ApplyAll();
        }

        /// <summary>启动后自动查找并附加 ARAD.EXE；失败只更新状态栏，不弹阻塞对话框。</summary>
        public void AutoAttach()
        {
            Connect();
        }

        public void ApplyAll()
        {
            ApplyPickup();
            ApplyEquip();
            ApplyTraverse();
            ApplyBuff();
            ApplyAutoItem();
            ApplyHelper();
        }

        public void SaveConfig()
        {
            ConfigService.Save(Config);
        }

        /// <summary>配置改动后：先落盘，再把规则重新下发内部运行时。</summary>
        public void SaveAndApply()
        {
            SaveConfig();
            ApplyAll();
        }

        public void HandleHotkey(int id)
        {
            // 热键回调统一回到 UI 线程再改绑定属性，避免跨线程改绑定属性引发通知异常。
            var application = Application.Current;
            if (application != null && !application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.BeginInvoke(new Action(() => HandleHotkey(id)));
                return;
            }

            switch (id)
            {
                case HotkeyService.EquipId:
                    if (!Config.Equip.Enabled)
                    {
                        return;
                    }
                    if (!IsAttached)
                    {
                        return;
                    }
                    CoreBridge.EquipTrigger();
                    break;

                case HotkeyService.PickupToggleId:
                    if (!IsAttached)
                    {
                        return;
                    }
                    PickupEnabled = !Config.Pickup.Enabled;
                    break;

                case HotkeyService.LeftId:
                    TraverseHotkey(0);
                    break;
                case HotkeyService.RightId:
                    TraverseHotkey(1);
                    break;
                case HotkeyService.UpId:
                    TraverseHotkey(2);
                    break;
                case HotkeyService.DownId:
                    TraverseHotkey(3);
                    break;

                case HotkeyService.LocalGmId:
                    ToggleLocalGm();
                    break;

                case HotkeyService.BuffId:
                    if (!IsAttached)
                    {
                        return;
                    }
                    CoreBridge.TriggerBuff();
                    CoreBridge.Announce("手动BUFF 已执行");
                    break;

                case HotkeyService.AutoItemId:
                    if (!IsAttached)
                    {
                        return;
                    }
                    CoreBridge.TriggerAutoItem();
                    CoreBridge.Announce("手动物品 已执行");
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _statusTimer.Stop();
            _statusTimer.Tick -= OnStatusTick;
            lock (_delayLock)
            {
                _enterPending = 0;
                _exitPending = 0;
                if (_enterDelayTimer != null)
                {
                    _enterDelayTimer.Dispose();
                    _enterDelayTimer = null;
                }
                if (_exitDelayTimer != null)
                {
                    _exitDelayTimer.Dispose();
                    _exitDelayTimer = null;
                }
                if (_helperDelayTimer != null)
                {
                    _helperDelayTimer.Dispose();
                    _helperDelayTimer = null;
                }
            }
        }

        private static string ProgramVersion()
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version == null)
            {
                return "1.1.0";
            }
            // 程序集版本天然是四段，界面只显示前三段。
            return string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
        }

        /// <summary>折叠面板状态写回配置；返回值表示是否真的发生变化。</summary>
        private bool SetExpanded(bool value, bool current, Action<bool> assign, string propertyName)
        {
            if (current == value)
            {
                return false;
            }
            assign(value);
            OnPropertyChanged(propertyName);
            SaveConfig();
            return true;
        }

        private void TraverseHotkey(int dir)
        {
            if (!Config.Traverse.Enabled)
            {
                return;
            }
            CoreBridge.TraverseStep(dir);
        }

        /// <summary>ALT+G：切换本地 GM 权限桩（补丁写入成功才翻转状态）。</summary>
        private void ToggleLocalGm()
        {
            if (!CoreBridge.IsAttached())
            {
                return;
            }

            bool enabled;
            string note;
            if (!CoreBridge.ToggleLocalGm(out enabled, out note))
            {
                return;
            }
            CoreBridge.Announce(enabled ? "本地GM 已启用" : "本地GM 已关闭");
        }

        /// <summary>查找 ARAD.EXE 并附加。</summary>
        private void Connect()
        {
            var target = ProcessService.FindByExeName(TargetExeName);
            if (target == null)
            {
                StatusText = "未附加：未找到 ARAD.EXE 进程";
                return;
            }

            string error;
            if (!CoreBridge.Attach(target.Pid, out error))
            {
                StatusText = "未附加：" + (string.IsNullOrEmpty(error) ? "附加失败" : error);
                return;
            }

            _attachedPid = target.Pid;
            _startupAnnounced = false;
            IsAttached = true;
            StatusText = "已附加 · PID " + target.Pid;
            ApplyAll();

            // 附加成功后自动扫描一次装备栏，供豁免列表展示真实数据。
            Equip.Refresh();
        }

        private void ToggleTheme()
        {
            IsLightTheme = !IsLightTheme;
        }

        private void OnStatusTick(object sender, EventArgs e)
        {
            if (!IsAttached)
            {
                CancelDungeonEnter();
                CancelDungeonExit();
                Volatile.Write(ref _lastGameState, int.MinValue);
                ResetCharacterInfo();
                return;
            }

            if (!ProcessService.IsAlive(_attachedPid))
            {
                CoreBridge.Detach();
                IsAttached = false;
                _attachedPid = 0;
                CancelDungeonEnter();
                CancelDungeonExit();
                Volatile.Write(ref _lastGameState, int.MinValue);
                StatusText = "未附加：游戏进程已退出（可点击「重连」）";
                ResetCharacterInfo();
                return;
            }

            CoreStatus status;
            if (!CoreBridge.TryGetStatus(out status))
            {
                return;
            }

            AnnounceStartupTime(status.GameState);

            var prefix = "已附加 · PID " + _attachedPid;
            if (status.GameState == 0)
            {
                StatusText = prefix + " · 未进入游戏";
            }
            else if (status.InDungeon != 0)
            {
                StatusText = prefix + " · 副本 · 等级 " + status.Level + " · 房间 (" + status.RoomX + "," +
                             status.RoomY + ")";
            }
            else
            {
                StatusText = prefix + " · 城镇 " + status.Town + " · 区域 " + status.Area + " · 等级 " +
                             status.Level;
            }

            UpdateCharacterInfo(status);
            HandleStateTransition(status);
        }

        /// <summary>
        /// 状态边沿检测（挂在 1s 状态轮询上，不新增轮询线程）：
        /// 只在 GameState 数值发生变化时触发一次，同一状态内不会每秒重复。
        /// 首次采样只建立基线；若首次即为副本(3)（例如附加时已在图内），按"切入副本"处理。
        /// 非副本 → 副本(3)：延迟 AutoTriggerDelayMs 后执行自动物品；
        /// 副本(3) → 城镇(1/2)：延迟 AutoTriggerDelayMs 后执行装备处理。
        /// 两条延迟链路各自独立：切入副本作废待执行的装备处理，离开副本作废待执行的自动物品；
        /// 快速反复进出时只有最后一次生效，不排队堆积。
        /// 触发一律异步执行，不阻塞 UI 线程；执行前用 CoreBridge.CurrentGameState() 直读状态做最终校验。
        /// </summary>
        private void HandleStateTransition(CoreStatus status)
        {
            var state = status.GameState;
            var previous = Volatile.Read(ref _lastGameState);

            if (previous == int.MinValue)
            {
                Volatile.Write(ref _lastGameState, state);
                if (state == 3)
                {
                    ScheduleDungeonEnter();
                }
                return;
            }

            if (state == previous)
            {
                return;
            }
            Volatile.Write(ref _lastGameState, state);

            if (state == 3)
            {
                CancelDungeonExit();
                ScheduleDungeonEnter();
                return;
            }

            CancelDungeonEnter();
            CancelDungeonExit();
            if (previous == 3 && (state == 1 || state == 2))
            {
                ScheduleDungeonExit();
            }
        }

        /// <summary>安排"进入副本 AutoTriggerDelayMs 后"的自动物品；重复调用只保留最后一次。</summary>
        private void ScheduleDungeonEnter()
        {
            lock (_delayLock)
            {
                _enterPending = 1;
                if (_enterDelayTimer == null)
                {
                    _enterDelayTimer = new Timer(OnDungeonEnterElapsed, null, AutoTriggerDelayMs, Timeout.Infinite);
                }
                else
                {
                    _enterDelayTimer.Change(AutoTriggerDelayMs, Timeout.Infinite);
                }
            }
        }

        /// <summary>安排"副本→城镇 AutoTriggerDelayMs 后"的自动装备处理；重复调用只保留最后一次。</summary>
        private void ScheduleDungeonExit()
        {
            if (!Config.Equip.Enabled)
            {
                return;
            }
            lock (_delayLock)
            {
                _exitPending = 1;
                if (_exitDelayTimer == null)
                {
                    _exitDelayTimer = new Timer(OnDungeonExitElapsed, null, AutoTriggerDelayMs, Timeout.Infinite);
                }
                else
                {
                    _exitDelayTimer.Change(AutoTriggerDelayMs, Timeout.Infinite);
                }
            }
        }

        /// <summary>作废待执行的自动物品触发（离开副本 / 分离进程 / 界面释放时调用）。</summary>
        private void CancelDungeonEnter()
        {
            lock (_delayLock)
            {
                _enterPending = 0;
                if (_enterDelayTimer != null)
                {
                    _enterDelayTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        /// <summary>作废待执行的装备处理自动触发（切回副本 / 分离进程 / 界面释放时调用）。</summary>
        private void CancelDungeonExit()
        {
            lock (_delayLock)
            {
                _exitPending = 0;
                if (_exitDelayTimer != null)
                {
                    _exitDelayTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        private void OnDungeonEnterElapsed(object state)
        {
            lock (_delayLock)
            {
                if (_enterPending == 0)
                {
                    return;
                }
                _enterPending = 0;
            }

            // 执行前直读最新状态：已离开副本(3) 就静默丢弃，不补发。
            if (CoreBridge.CurrentGameState() != 3)
            {
                return;
            }

            TriggerDungeonEnter();
        }

        private void OnDungeonExitElapsed(object state)
        {
            lock (_delayLock)
            {
                if (_exitPending == 0)
                {
                    return;
                }
                _exitPending = 0;
            }

            // 执行前直读最新状态：必须是城镇(1/2)，否则静默返回。
            var current = CoreBridge.CurrentGameState();
            if (current != 1 && current != 2)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => CoreBridge.EquipTrigger());
        }

        private void TriggerDungeonEnter()
        {
            if (Config.AutoItem.Enabled)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    CoreBridge.TriggerAutoItem();
                    CoreBridge.Announce("自动物品 已执行");
                });
            }
        }

        /// <summary>
        /// 附加成功后首次进入可玩状态（1 城镇 / 2 选图 / 3 副本）时播报一次当前时间 HH:mm:ss；
        /// 一次性标志，重新附加时复位，之后不再重复。
        /// </summary>
        private void AnnounceStartupTime(int gameState)
        {
            if (_startupAnnounced)
            {
                return;
            }
            if (gameState != 1 && gameState != 2 && gameState != 3)
            {
                return;
            }
            _startupAnnounced = true;
            CoreBridge.Announce(DateTime.Now.ToString("HH:mm:ss"));
        }

        /// <summary>按 1s 状态轮询刷新角色信息区；未附加不展示任何数据。</summary>
        private void UpdateCharacterInfo(CoreStatus status)
        {
            _autoCharacterId = status.AutoCharacterId;
            RefreshCharacterIdReadText();

            var name = string.IsNullOrEmpty(status.Name) ? "—" : status.Name;
            var job = string.IsNullOrEmpty(status.Job) ? "—" : status.Job;
            CharacterLine = "名称: " + name + "   等级: " + status.Level + "   职业: " + job +
                            "   当前状态: " + GameStateText(status.GameState);

            if (status.InDungeon != 0)
            {
                var dungeonName = string.IsNullOrEmpty(status.DungeonName) ? "名称不可用" : status.DungeonName;
                var boss = status.HasBoss != 0
                    ? "X=" + status.BossX + " Y=" + status.BossY
                    : "—";
                LocationLine = "副本ID: " + status.DungeonId + "   副本名称: " + dungeonName;
                RoomLine = "当前房间 X=" + status.RoomX + " Y=" + status.RoomY + "   BOSS房间 " + boss;
                HasLocationLine = true;
                HasRoomLine = true;
                return;
            }

            if (status.GameState == 1 || status.GameState == 2)
            {
                LocationLine = "城镇ID: " + status.Town + "   区域ID: " + status.Area +
                               "   坐标 X=" + status.PosX.ToString("F0") + " Y=" + status.PosY.ToString("F0");
                RoomLine = string.Empty;
                HasLocationLine = true;
                HasRoomLine = false;
                return;
            }

            LocationLine = string.Empty;
            RoomLine = string.Empty;
            HasLocationLine = false;
            HasRoomLine = false;
        }

        private void ResetCharacterInfo()
        {
            CharacterLine = "未附加";
            LocationLine = string.Empty;
            RoomLine = string.Empty;
            HasLocationLine = false;
            HasRoomLine = false;
            _autoCharacterId = 0;
            RefreshCharacterIdReadText();
        }

        /// <summary>角色ID 回显：只显示自动读取结果，未进入角色/读取失败为「—」。</summary>
        private void RefreshCharacterIdReadText()
        {
            CharacterIdReadText = _autoCharacterId > 0
                ? _autoCharacterId.ToString(CultureInfo.InvariantCulture)
                : "—";
        }

        private static string GameStateText(int state)
        {
            switch (state)
            {
                case 0: return "选择角色";
                case 1: return "城镇";
                case 2: return "城镇（选图）";
                case 3: return "副本中";
                case 5: return "选择频道";
                case 9: return "编号塔";
                case 10: return "极限模式";
                case 19: return "无尽模式";
                default: return "状态 " + state;
            }
        }

        private void ApplyPickup()
        {
            CoreBridge.SetPickup(
                Config.Pickup.Enabled,
                (int)Config.Pickup.Mode,
                Config.Pickup.IntervalMs);
        }

        private void ApplyEquip()
        {
            var exempt = new int[Config.Equip.Exempt.Length];
            for (int i = 0; i < exempt.Length; i++)
            {
                exempt[i] = Config.Equip.Exempt[i] ? 1 : 0;
            }
            CoreBridge.SetEquip(Config.Equip.Enabled, Config.Equip.Actions, exempt);
        }

        private void ApplyTraverse()
        {
            CoreBridge.SetTraverse(Config.Traverse.Enabled, (int)Config.Traverse.Mode);
        }

        /// <summary>BUFF 类型列表：随编辑框即时下发，供 ALT+1 单次触发使用（无自动触发）。</summary>
        private void ApplyBuff()
        {
            CoreBridge.SetBuff(Config.Buff.Types);
        }

        /// <summary>自动物品：开关只表示"是否响应切入副本的自动触发"，单次执行由状态边沿或 ALT+2 发起。</summary>
        private void ApplyAutoItem()
        {
            CoreBridge.SetAutoItem(Config.AutoItem.Enabled, Config.AutoItem.Ids);
        }

        private void ApplyHelper()
        {
            CoreBridge.SetHelper(Config.Helper.PickupEnabled, Config.Helper.HellEnabled);
        }

        /// <summary>
        /// 助手开关的界面侧去抖：与上一次下发至少间隔 1 秒，避免撞上服务端与聊天共用的
        /// 1 秒 5 条限流；间隔不足时延后到窗口末尾再下发，保留最后一次状态。
        /// </summary>
        private void RequestHelperApply()
        {
            var now = Environment.TickCount;
            var elapsed = now - _lastHelperApplyTick;
            if (_lastHelperApplyTick == 0 || elapsed < 0 || elapsed >= HelperApplyGapMs)
            {
                _lastHelperApplyTick = now;
                ApplyHelper();
                return;
            }

            lock (_delayLock)
            {
                var wait = HelperApplyGapMs - elapsed;
                if (_helperDelayTimer == null)
                {
                    _helperDelayTimer = new Timer(OnHelperDelayElapsed, null, wait, Timeout.Infinite);
                }
                else
                {
                    _helperDelayTimer.Change(wait, Timeout.Infinite);
                }
            }
        }

        private void OnHelperDelayElapsed(object state)
        {
            _lastHelperApplyTick = Environment.TickCount;
            ApplyHelper();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
