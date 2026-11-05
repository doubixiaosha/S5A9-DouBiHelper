using System;
using System.Windows;

namespace DnfHelper.UI.Services
{
    /// <summary>深浅主题切换：只替换第一个合并字典（Colors.*.xaml），不重建界面。</summary>
    public static class ThemeService
    {
        public static void Apply(string theme)
        {
            var name = theme == "Light" ? "Light" : "Dark";
            var dictionary = new ResourceDictionary
            {
                Source = new Uri("Styles/Colors." + name + ".xaml", UriKind.Relative),
            };
            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count > 0)
            {
                merged[0] = dictionary;
            }
            else
            {
                merged.Add(dictionary);
            }
        }
    }
}
