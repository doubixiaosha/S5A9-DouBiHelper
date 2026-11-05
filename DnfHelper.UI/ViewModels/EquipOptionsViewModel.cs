using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DnfHelper.UI.Models;
using DnfHelper.UI.Services;

namespace DnfHelper.UI.ViewModels
{
    /// <summary>装备处理折叠区的六类型规则行：品质名 + 处理方式下拉。</summary>
    public sealed class EquipActionRowViewModel : INotifyPropertyChanged
    {
        private readonly int[] _actions;
        private readonly int _index;
        private readonly Action _onChanged;

        public EquipActionRowViewModel(int index, string qualityName, int[] actions, Action onChanged)
        {
            _index = index;
            QualityName = qualityName;
            _actions = actions;
            _onChanged = onChanged;
            GroupName = "EquipQualityAction" + index;
        }

        public string QualityName { get; }

        /// <summary>单选组名：每个品质一行，三选一互斥。</summary>
        public string GroupName { get; }

        public bool IsIgnore
        {
            get => SelectedIndex == 0;
            set { if (value) { SelectedIndex = 0; } }
        }

        public bool IsSell
        {
            get => SelectedIndex == 1;
            set { if (value) { SelectedIndex = 1; } }
        }

        public bool IsDisassemble
        {
            get => SelectedIndex == 2;
            set { if (value) { SelectedIndex = 2; } }
        }

        public int SelectedIndex
        {
            get => _actions[_index];
            set
            {
                var action = value < 0 || value > 2 ? 0 : value;
                if (_actions[_index] == action)
                {
                    return;
                }
                _actions[_index] = action;
                OnPropertyChanged(nameof(SelectedIndex));
                OnPropertyChanged(nameof(IsIgnore));
                OnPropertyChanged(nameof(IsSell));
                OnPropertyChanged(nameof(IsDisassemble));
                if (_onChanged != null)
                {
                    _onChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>装备栏单件豁免行：勾选表示该单件不处理。</summary>
    public sealed class EquipSlotCellViewModel : INotifyPropertyChanged
    {
        private readonly bool[] _exempt;
        private readonly int _index;
        private readonly Action _onChanged;
        private bool _isChecked;
        private string _name;
        private int _id;

        public EquipSlotCellViewModel(int index, int slot, string name, bool[] exempt, Action onChanged)
        {
            _index = index;
            Slot = slot;
            _name = name ?? string.Empty;
            _exempt = exempt;
            _onChanged = onChanged;
            _isChecked = exempt[index];
        }

        public int Slot { get; }
        public string Name => _name;
        public bool Occupied => !string.IsNullOrEmpty(_name) || _id != 0;

        public string DisplayName => Occupied ? Name : "—";

        public string IdText => _id != 0 ? _id.ToString() : "—";

        public string TooltipText => Occupied
            ? Name + "\nID " + _id + " / 槽位 " + Slot + "（勾选 = 该单件不处理）"
            : "空槽位 " + Slot + "（勾选 = 预留豁免）";

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                {
                    return;
                }
                _isChecked = value;
                _exempt[_index] = value;
                OnPropertyChanged();
                if (_onChanged != null)
                {
                    _onChanged();
                }
            }
        }

        /// <summary>刷新扫描到的单件；空名称/ID 表示该槽位没有装备（豁免配置仍按槽位保留）。</summary>
        public void SetItem(int id, string name)
        {
            var value = name ?? string.Empty;
            if (_id == id && _name == value)
            {
                return;
            }
            _id = id;
            _name = value;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Occupied));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IdText));
            OnPropertyChanged(nameof(TooltipText));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>装备处理折叠区：六品质动作 + 扫描到的装备豁免列表，改动即时写回 AppConfig 并下发内部运行时。</summary>
    public sealed class EquipOptionsViewModel : INotifyPropertyChanged
    {
        private readonly AppConfig _config;
        private readonly Action _onChanged;
        private readonly List<EquipSlotCellViewModel> _slots = new List<EquipSlotCellViewModel>();
        private string _summary = string.Empty;
        private string _emptyText = "未附加或未扫描到装备";

        public EquipOptionsViewModel(AppConfig config, Action onChanged)
        {
            _config = config;
            _onChanged = onChanged;
            RefreshCommand = new RelayCommand(Refresh);

            var names = new[] { "白装", "蓝装", "紫装", "粉装", "史诗", "传说" };
            for (int i = 0; i < names.Length; i++)
            {
                Rows.Add(new EquipActionRowViewModel(i, names[i], _config.Equip.Actions, _onChanged));
            }

            // 56 格豁免按槽位固定存储（索引 = 槽位 - 1），列表只展示扫描到装备的行。
            for (int i = 0; i < 56; i++)
            {
                _slots.Add(new EquipSlotCellViewModel(i, i + 1, string.Empty, _config.Equip.Exempt, _onChanged));
            }

            Refresh();
        }

        public ObservableCollection<EquipActionRowViewModel> Rows { get; } =
            new ObservableCollection<EquipActionRowViewModel>();

        /// <summary>当前扫描到的装备行，按槽位升序。</summary>
        public ObservableCollection<EquipSlotCellViewModel> VisibleSlots { get; } =
            new ObservableCollection<EquipSlotCellViewModel>();

        public ICommand RefreshCommand { get; }

        public bool HasItems => VisibleSlots.Count > 0;

        public string Summary
        {
            get => _summary;
            private set
            {
                if (_summary == value)
                {
                    return;
                }
                _summary = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
            }
        }

        public string EmptyText
        {
            get => _emptyText;
            private set
            {
                if (_emptyText == value)
                {
                    return;
                }
                _emptyText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EmptyText)));
            }
        }

        /// <summary>重新扫描装备栏，只为扫描到物品的槽位生成行；豁免勾选按槽位回填保持。</summary>
        public void Refresh()
        {
            var attached = CoreBridge.IsAttached();
            var items = attached ? CoreBridge.ScanEquip() : Array.Empty<CoreItemView>();

            VisibleSlots.Clear();
            foreach (var item in items)
            {
                var index = item.Slot - 1;
                if (index < 0 || index >= _slots.Count)
                {
                    continue;
                }
                var cell = _slots[index];
                cell.SetItem(item.Id, item.Name);
                VisibleSlots.Add(cell);
            }

            Summary = attached
                ? "装备栏 · 扫描到 " + VisibleSlots.Count + " 件 · 勾选 = 豁免（不处理）"
                : "未附加游戏进程，无法扫描装备；豁免仍按槽位保存。";
            EmptyText = attached
                ? "未扫描到装备（可点击「刷新」重试）"
                : "未附加或未扫描到装备";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasItems)));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
