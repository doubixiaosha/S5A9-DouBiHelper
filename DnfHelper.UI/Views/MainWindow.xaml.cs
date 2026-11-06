using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DnfHelper.UI.Services;
using DnfHelper.UI.ViewModels;

namespace DnfHelper.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly HotkeyService _hotkey = new HotkeyService();

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
            LoadTitleIcon();

            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
        }

        /// <summary>
        /// 标题区左上角图标：用 IconBitmapDecoder 取 ico 的多尺寸帧（优先 32×32），
        /// 避免直接 BitmapImage 取帧导致拉伸发虚。
        /// </summary>
        private void LoadTitleIcon()
        {
            try
            {
                var decoder = new IconBitmapDecoder(
                    new Uri("pack://application:,,,/Assets/DNF.ico", UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                var frame = PickIconFrame(decoder.Frames);
                if (frame != null)
                {
                    TitleIcon.Source = frame;
                }
            }
            catch (Exception)
            {
                // 图标资源缺失不影响启动，标题区留空。
            }
        }

        private static BitmapFrame PickIconFrame(IList<BitmapFrame> frames)
        {
            BitmapFrame best = null;
            foreach (var frame in frames)
            {
                if (best == null ||
                    (frame.PixelWidth >= 32 && (best.PixelWidth < 32 || frame.PixelWidth < best.PixelWidth)))
                {
                    best = frame;
                }
            }
            return best;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            _hotkey.Register(handle);
            _hotkey.Pressed += id => _viewModel.HandleHotkey(id);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _hotkey.Dispose();
            _viewModel.SaveConfig();
            _viewModel.Dispose();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
