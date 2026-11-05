using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DnfHelper.UI.Models
{
    /// <summary>
    /// DouBi.ini 读写。绿色免安装：配置固定落在 exe 同目录，内部运行时通过内存接口接收配置。
    /// 段名与键名统一为中文，文件按 UTF-8（无 BOM）读写，不依赖系统 ANSI 代码页。
    /// 只持久化主题与各功能开关/参数；窗口尺寸、折叠状态、豁免 56 格、扫描间隔等不写入。
    /// 只认中文段/中文键与中文取值；其它段、键、取值读取时忽略，缺失即用内置默认值，
    /// 下次保存自然丢弃。
    /// </summary>
    public static class ConfigService
    {
        private const string SectionGeneral = "常规";
        private const string SectionPickup = "捡物";
        private const string SectionEquip = "装备处理";
        private const string SectionTraverse = "顺图";
        private const string SectionSpecial = "特殊功能";

        private const string DefaultTheme = "Dark";
        private const string DefaultBuffTypes = BuffSettings.DefaultTypes;
        private const string DefaultItemIds = AutoItemSettings.DefaultIds;

        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DouBi.ini");

        public static AppConfig Load(string path = null)
        {
            path = path ?? ConfigPath;
            var config = new AppConfig();
            if (!File.Exists(path))
            {
                // 首次运行生成带默认值的 DouBi.ini，保证绿色包落地即可见配置。
                Save(config, path);
                return config;
            }

            try
            {
                var sections = Parse(File.ReadAllLines(path, Encoding.UTF8));
                ReadGeneral(sections, config);
                ReadPickup(sections, config);
                ReadEquip(sections, config);
                ReadTraverse(sections, config);
                ReadBuff(sections, config);
                ReadAutoItem(sections, config);
            }
            catch (Exception)
            {
                // 配置损坏不影响启动：按默认值运行，下次保存时重建。
                return new AppConfig();
            }
            return config;
        }

        public static bool Save(AppConfig config, string path = null)
        {
            path = path ?? ConfigPath;
            try
            {
                var builder = new StringBuilder();
                builder.AppendLine("[" + SectionGeneral + "]");
                builder.AppendLine("主题=" + (config.Theme == "Light" ? "浅色" : "深色"));
                builder.AppendLine();

                builder.AppendLine("[" + SectionPickup + "]");
                builder.AppendLine("启用=" + Bool(config.Pickup.Enabled));
                builder.AppendLine("方式=" + PickupModeText(config.Pickup.Mode));
                builder.AppendLine();

                builder.AppendLine("[" + SectionEquip + "]");
                builder.AppendLine("启用=" + Bool(config.Equip.Enabled));
                var qualities = new[] { "白装动作", "蓝装动作", "紫装动作", "粉装动作", "史诗动作", "传说动作" };
                for (int i = 0; i < qualities.Length; i++)
                {
                    var action = i < config.Equip.Actions.Length ? config.Equip.Actions[i] : 0;
                    builder.AppendLine(qualities[i] + "=" + EquipActionText(action));
                }
                builder.AppendLine();

                builder.AppendLine("[" + SectionTraverse + "]");
                builder.AppendLine("启用=" + Bool(config.Traverse.Enabled));
                builder.AppendLine("方式=" + TraverseModeText(config.Traverse.Mode));
                builder.AppendLine();

                builder.AppendLine("[" + SectionSpecial + "]");
                builder.AppendLine("BUFF类型=" + Escape(config.Buff.Types));
                builder.AppendLine("物品ID=" + Escape(config.AutoItem.Ids));

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Dictionary<string, Dictionary<string, string>> Parse(string[] lines)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> current = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim().Trim('\uFEFF');
                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                {
                    continue;
                }
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    var name = line.Substring(1, line.Length - 2).Trim();
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    result[name] = current;
                    continue;
                }
                if (current == null)
                {
                    continue;
                }
                var index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }
                current[line.Substring(0, index).Trim()] = line.Substring(index + 1).Trim();
            }
            return result;
        }

        private static void ReadGeneral(Dictionary<string, Dictionary<string, string>> sections, AppConfig config)
        {
            config.Theme = ParseTheme(ReadString(sections, SectionGeneral, "主题", DefaultTheme));
        }

        private static void ReadPickup(Dictionary<string, Dictionary<string, string>> sections, AppConfig config)
        {
            config.Pickup.Enabled = ReadBool(sections, SectionPickup, "启用", false);
            config.Pickup.Mode = ParsePickupMode(ReadString(sections, SectionPickup, "方式", "吸物"));
        }

        private static void ReadEquip(Dictionary<string, Dictionary<string, string>> sections, AppConfig config)
        {
            config.Equip.Enabled = ReadBool(sections, SectionEquip, "启用", false);

            var keys = new[] { "白装动作", "蓝装动作", "紫装动作", "粉装动作", "史诗动作", "传说动作" };
            for (int i = 0; i < config.Equip.Actions.Length; i++)
            {
                config.Equip.Actions[i] = ParseEquipAction(ReadString(sections, SectionEquip, keys[i], "不处理"));
            }
        }

        private static void ReadTraverse(Dictionary<string, Dictionary<string, string>> sections, AppConfig config)
        {
            config.Traverse.Enabled = ReadBool(sections, SectionTraverse, "启用", false);
            config.Traverse.Mode = ParseTraverseMode(ReadString(sections, SectionTraverse, "方式", "组包"));
        }

        private static void ReadBuff(Dictionary<string, Dictionary<string, string>> sections, AppConfig config)
        {
            config.Buff.Types = ReadString(sections, SectionSpecial, "BUFF类型", DefaultBuffTypes);
        }

        private static void ReadAutoItem(Dictionary<string, Dictionary<string, string>> sections, AppConfig config)
        {
            config.AutoItem.Ids = ReadString(sections, SectionSpecial, "物品ID", DefaultItemIds);
        }

        /// <summary>只取当前中文段/中文键，缺失或键不存在时用内置默认值。</summary>
        private static string ReadString(
            Dictionary<string, Dictionary<string, string>> sections,
            string section, string key, string fallback)
        {
            string value;
            if (sections.TryGetValue(section, out var current) && current.TryGetValue(key, out value))
            {
                return Unescape(value);
            }
            return fallback;
        }

        /// <summary>布尔只认「是/否」，其它取值一律回落到 fallback。</summary>
        private static bool ReadBool(
            Dictionary<string, Dictionary<string, string>> sections,
            string section, string key, bool fallback)
        {
            string value;
            if (sections.TryGetValue(section, out var current) && current.TryGetValue(key, out value))
            {
                return ParseBool(value, fallback);
            }
            return fallback;
        }

        private static bool ParseBool(string text, bool fallback)
        {
            var value = (text ?? string.Empty).Trim();
            if (value == "是")
            {
                return true;
            }
            if (value == "否")
            {
                return false;
            }
            return fallback;
        }

        private static string ParseTheme(string text)
        {
            return (text ?? string.Empty).Trim() == "浅色" ? "Light" : "Dark";
        }

        private static PickupMode ParsePickupMode(string text)
        {
            return (text ?? string.Empty).Trim() == "自动入包" ? PickupMode.AutoSend : PickupMode.Absorb;
        }

        private static TraverseMode ParseTraverseMode(string text)
        {
            switch ((text ?? string.Empty).Trim())
            {
                case "强制": return TraverseMode.Force;
                case "瞬移": return TraverseMode.Teleport;
                case "移动": return TraverseMode.Move;
                default: return TraverseMode.Packet;
            }
        }

        private static int ParseEquipAction(string text)
        {
            switch ((text ?? string.Empty).Trim())
            {
                case "卖物": return 1;
                case "分解": return 2;
                default: return 0;
            }
        }

        private static string PickupModeText(PickupMode mode)
        {
            return mode == PickupMode.AutoSend ? "自动入包" : "吸物";
        }

        private static string TraverseModeText(TraverseMode mode)
        {
            switch (mode)
            {
                case TraverseMode.Force: return "强制";
                case TraverseMode.Teleport: return "瞬移";
                case TraverseMode.Move: return "移动";
                default: return "组包";
            }
        }

        private static string EquipActionText(int action)
        {
            switch (action)
            {
                case 1: return "卖物";
                case 2: return "分解";
                default: return "不处理";
            }
        }

        private static string Bool(bool value)
        {
            return value ? "是" : "否";
        }

        private static string Escape(string text)
        {
            return (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        }

        private static string Unescape(string text)
        {
            return text ?? string.Empty;
        }
    }
}
