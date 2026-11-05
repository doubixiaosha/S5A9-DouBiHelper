using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DnfHelper.UI.Services
{
    /// <summary>
    /// 全局热键：ALT+F 装备处理一次；ALT+方向键顺图；ALT+G 本地 GM 切换；ALT+1/ALT+2 BUFF / 物品各执行一次；
    /// ALT+Q 切换自动拾取开关（等同点击捡物卡的启用开关）。
    /// 热键常驻注册，是否响应由界面侧判断（自动物品的开关不影响热键单次触发）。
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        public const int EquipId = 9001;
        public const int LeftId = 9002;
        public const int RightId = 9003;
        public const int UpId = 9004;
        public const int DownId = 9005;
        public const int LocalGmId = 9006;
        public const int BuffId = 9007;
        public const int AutoItemId = 9008;
        public const int PickupToggleId = 9009;

        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint VkF = 0x46;
        private const uint VkG = 0x47;
        private const uint VkQ = 0x51;
        private const uint Vk1 = 0x31;
        private const uint Vk2 = 0x32;
        private const uint VkLeft = 0x25;
        private const uint VkUp = 0x26;
        private const uint VkRight = 0x27;
        private const uint VkDown = 0x28;

        private HwndSource _source;
        private IntPtr _handle;
        private bool _registered;

        public event Action<int> Pressed;
        public string LastError { get; private set; } = string.Empty;

        public bool Register(IntPtr handle)
        {
            Unregister();
            _handle = handle;
            _source = HwndSource.FromHwnd(handle);
            if (_source == null)
            {
                LastError = "无法获取窗口句柄";
                return false;
            }
            _source.AddHook(WndProc);

            var ok = RegisterHotKey(handle, EquipId, ModAlt, VkF)
                     && RegisterHotKey(handle, LeftId, ModAlt, VkLeft)
                     && RegisterHotKey(handle, RightId, ModAlt, VkRight)
                     && RegisterHotKey(handle, UpId, ModAlt, VkUp)
                     && RegisterHotKey(handle, DownId, ModAlt, VkDown)
                     && RegisterHotKey(handle, LocalGmId, ModAlt, VkG)
                     && RegisterHotKey(handle, BuffId, ModAlt, Vk1)
                     && RegisterHotKey(handle, AutoItemId, ModAlt, Vk2)
                     && RegisterHotKey(handle, PickupToggleId, ModAlt, VkQ);
            if (!ok)
            {
                LastError = "ALT+F / ALT+方向键 / ALT+G / ALT+1 / ALT+2 / ALT+Q 注册失败，可能已被其它程序占用";
                Unregister();
                return false;
            }
            _registered = true;
            return true;
        }

        public void Unregister()
        {
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }
            if (_registered && _handle != IntPtr.Zero)
            {
                UnregisterHotKey(_handle, EquipId);
                UnregisterHotKey(_handle, LeftId);
                UnregisterHotKey(_handle, RightId);
                UnregisterHotKey(_handle, UpId);
                UnregisterHotKey(_handle, DownId);
                UnregisterHotKey(_handle, LocalGmId);
                UnregisterHotKey(_handle, BuffId);
                UnregisterHotKey(_handle, AutoItemId);
                UnregisterHotKey(_handle, PickupToggleId);
            }
            _registered = false;
        }

        public void Dispose()
        {
            Unregister();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey)
            {
                handled = true;
                var handler = Pressed;
                if (handler != null)
                {
                    handler(wParam.ToInt32());
                }
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
