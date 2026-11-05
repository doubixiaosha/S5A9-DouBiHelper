using System.Collections.Generic;

namespace DnfHelper.UI.Core.Shared
{
    /// <summary>把“逗号/空格/分号分隔的编号串”解析成正整数列表（BUFF 类型、自动物品 ID 共用）。</summary>
    internal static class IdList
    {
        internal static int[] Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new int[0];
            }
            var values = new List<int>();
            var token = string.Empty;
            for (var i = 0; i <= text.Length; i++)
            {
                var c = i < text.Length ? text[i] : ' ';
                if (c >= '0' && c <= '9')
                {
                    token += c;
                    continue;
                }
                if (token.Length > 0)
                {
                    int value;
                    if (int.TryParse(token, out value) && value > 0 && !values.Contains(value))
                    {
                        values.Add(value);
                    }
                    token = string.Empty;
                }
            }
            return values.ToArray();
        }
    }
}
