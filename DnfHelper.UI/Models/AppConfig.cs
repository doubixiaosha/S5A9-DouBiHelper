using System.Collections.Generic;

namespace DnfHelper.UI.Models
{
    /// <summary>捡物实现方式；取值对应 Core 的 PickupConfig.mode。</summary>
    public enum PickupMode
    {
        Absorb = 1,
        AutoSend = 2,
    }

    /// <summary>顺图方式；取值对应 Core 的 TraverseConfig.mode（4 为占位，未实现）。</summary>
    public enum TraverseMode
    {
        Packet = 1,
        Force = 2,
        Teleport = 3,
        Move = 4,
    }

    /// <summary>装备处理方式；取值对应 Core 的 EquipConfig.actions（0 不处理 / 1 卖物 / 2 分解）。</summary>
    public enum EquipAction
    {
        Ignore = 0,
        Sell = 1,
        Disassemble = 2,
    }

    public sealed class PickupSettings
    {
        public bool Enabled { get; set; }
        public PickupMode Mode { get; set; } = PickupMode.Absorb;
        public int IntervalMs { get; set; } = 500;
    }

    public sealed class EquipSettings
    {
        public bool Enabled { get; set; }

        /// <summary>白/蓝/紫/粉/史诗/传说 六类型处理方式，取值 0 不处理 / 1 卖物 / 2 分解。</summary>
        public int[] Actions { get; set; } = new int[6];

        /// <summary>56 格装备的单件豁免标记，下标 = 槽位 - 1。</summary>
        public bool[] Exempt { get; set; } = new bool[56];
    }

    public sealed class TraverseSettings
    {
        public bool Enabled { get; set; }
        public TraverseMode Mode { get; set; } = TraverseMode.Packet;
    }

    /// <summary>BUFF：Types 为 BUFF 类型编号列表（逗号/空格分隔）；仅 ALT+1 单次触发。</summary>
    public sealed class BuffSettings
    {
        /// <summary>内置默认 BUFF 类型列表（末尾 1219 重复两次，属预期，勿去重）。</summary>
        public const string DefaultTypes = "51,133,1072,1079,24,279,105,1061,127,164,1035,50,268,1219,1219";

        public string Types { get; set; } = DefaultTypes;
    }

    /// <summary>自动物品：Ids 为物品 ID 列表（逗号/空格分隔），进副本后自动使用一次。</summary>
    public sealed class AutoItemSettings
    {
        /// <summary>内置默认物品 ID 列表。</summary>
        public const string DefaultIds =
            "10004505,2682833,10004110,10004695,490010896,490703676,2600656,2683266,2600021,10004111," +
            "490003921,490010607,2683263,2683264,10007342,2749750,10002624,2680766,2600028,10089515";

        public bool Enabled { get; set; }
        public string Ids { get; set; } = DefaultIds;
    }

    /// <summary>
    /// 魔界冒险助手：两个开关只在服务端侧生效，发送成功不等于服务端已生效；
    /// 角色 ID 由运行时自动读取（GameReader.CharacterId），不再保存到配置。
    /// </summary>
    public sealed class HelperSettings
    {
        public bool PickupEnabled { get; set; }
        public bool HellEnabled { get; set; }
    }

    /// <summary>界面折叠状态，持久化后重启保持。</summary>
    public sealed class UiSettings
    {
        public bool PickupExpanded { get; set; }
        public bool EquipExpanded { get; set; }
    }

    public sealed class AppConfig
    {
        public string Theme { get; set; } = "Dark";
        public UiSettings Ui { get; } = new UiSettings();
        public PickupSettings Pickup { get; } = new PickupSettings();
        public EquipSettings Equip { get; } = new EquipSettings();
        public TraverseSettings Traverse { get; } = new TraverseSettings();
        public BuffSettings Buff { get; } = new BuffSettings();
        public AutoItemSettings AutoItem { get; } = new AutoItemSettings();
        public HelperSettings Helper { get; } = new HelperSettings();
    }
}
