namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// 单个静态字节补丁：首启读取原字节，关闭时写回，Detach 前由功能层主动还原。
    /// </summary>
    internal sealed class BytePatch
    {
        private const int MaxPatchBytes = 16;

        private readonly object _lock = new object();
        private uint _address;
        private readonly byte[] _patch = new byte[MaxPatchBytes];
        private readonly byte[] _original = new byte[MaxPatchBytes];
        private int _size;
        private bool _originalValid;
        private bool _applied;

        internal bool Applied => _applied;

        internal void SetTarget(uint address, byte[] bytes, int size)
        {
            lock (_lock)
            {
                _address = address;
                _size = size > MaxPatchBytes ? MaxPatchBytes : size;
                for (var i = 0; i < _size; i++)
                {
                    _patch[i] = bytes[i];
                }
                _originalValid = false;
                _applied = false;
            }
        }

        internal bool Apply(bool enabled)
        {
            lock (_lock)
            {
                if (_address == 0 || _size == 0)
                {
                    return false;
                }
                if (enabled == _applied)
                {
                    return true;
                }

                bool okay;
                if (enabled)
                {
                    if (!_originalValid)
                    {
                        var original = new byte[_size];
                        okay = RemoteProcess.Instance.ReadBytes(_address, original, _size);
                        if (okay)
                        {
                            for (var i = 0; i < _size; i++)
                            {
                                _original[i] = original[i];
                            }
                        }
                        _originalValid = okay;
                    }
                    else
                    {
                        okay = true;
                    }
                    okay = okay && Write(_patch, _size);
                }
                else
                {
                    okay = _originalValid && Write(_original, _size);
                }

                if (okay)
                {
                    _applied = enabled;
                }
                return okay;
            }
        }

        internal bool Restore()
        {
            lock (_lock)
            {
                var okay = _applied && _originalValid && Write(_original, _size);
                if (okay)
                {
                    _applied = false;
                }
                return okay;
            }
        }

        internal void Forget()
        {
            lock (_lock)
            {
                _originalValid = false;
                _applied = false;
            }
        }

        private bool Write(byte[] bytes, int size)
        {
            return RemoteProcess.Instance.WriteBytes(_address, bytes, size);
        }
    }
}
