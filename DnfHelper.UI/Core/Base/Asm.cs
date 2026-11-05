using System;
using System.Collections.Generic;

namespace DnfHelper.UI.Core.Base
{
    /// <summary>
    /// 远程 x86 机器码拼装。指令字节序列固定，改动前先按反汇编核对。
    /// </summary>
    internal sealed class Asm
    {
        private readonly List<byte> _code = new List<byte>(64);

        internal int Size => _code.Count;

        internal void Raw(byte[] data)
        {
            if (data != null && data.Length > 0)
            {
                _code.AddRange(data);
            }
        }

        internal void Byte(byte value)
        {
            _code.Add(value);
        }

        internal void Push(uint value)
        {
            _code.Add((byte)0x68);
            AppendUInt32(value);
        }

        internal void MovEcx(uint value)
        {
            _code.Add((byte)0xB9);
            AppendUInt32(value);
        }

        internal void MovEax(uint value)
        {
            _code.Add((byte)0xB8);
            AppendUInt32(value);
        }

        internal void CallEax()
        {
            _code.Add((byte)0xFF);
            _code.Add((byte)0xD0);
        }

        internal void PushAd()
        {
            _code.Add((byte)0x60);
        }

        internal void PopAd()
        {
            _code.Add((byte)0x61);
        }

        /// <summary>mov ecx, [addr]（绝对地址）。</summary>
        internal void MovEcxFromMem(uint address)
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0x0D);
            AppendUInt32(address);
        }

        /// <summary>mov eax, [ecx]（无偏移）。</summary>
        internal void MovEaxFromEcx()
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0x01);
        }

        /// <summary>mov ecx, [ecx]（无偏移）。</summary>
        internal void MovEcxFromEcx()
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0x09);
        }

        /// <summary>mov ecx, [eax + disp8]。</summary>
        internal void MovEcxFromEaxDisp8(byte displacement)
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0x48);
            _code.Add(displacement);
        }

        /// <summary>push eax。</summary>
        internal void PushEax()
        {
            _code.Add((byte)0x50);
        }

        /// <summary>mov edx, [ecx]（无偏移）。</summary>
        internal void MovEdxFromEcx()
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0x11);
        }

        /// <summary>mov eax, edx。</summary>
        internal void MovEaxEdx()
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0xC2);
        }

        /// <summary>add eax, imm32。</summary>
        internal void AddEax(uint value)
        {
            _code.Add((byte)0x05);
            AppendUInt32(value);
        }

        /// <summary>mov ebx, [eax]（无偏移）。</summary>
        internal void MovEbxFromEax()
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0x18);
        }

        /// <summary>mov ecx, eax。</summary>
        internal void MovEcxEax()
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0xC8);
        }

        /// <summary>mov esi, eax。</summary>
        internal void MovEsiEax()
        {
            _code.Add((byte)0x8B);
            _code.Add((byte)0xF0);
        }

        internal void MovEbx(uint value)
        {
            _code.Add((byte)0xBB);
            AppendUInt32(value);
        }

        internal void MovEdx(uint value)
        {
            _code.Add((byte)0xBA);
            AppendUInt32(value);
        }

        internal void MovEsi(uint value)
        {
            _code.Add((byte)0xBE);
            AppendUInt32(value);
        }

        internal void CallEbx()
        {
            _code.Add((byte)0xFF);
            _code.Add((byte)0xD3);
        }

        internal void CallEcx()
        {
            _code.Add((byte)0xFF);
            _code.Add((byte)0xD1);
        }

        internal void CallEdx()
        {
            _code.Add((byte)0xFF);
            _code.Add((byte)0xD2);
        }

        internal void CallEsi()
        {
            _code.Add((byte)0xFF);
            _code.Add((byte)0xD6);
        }

        internal void Int3()
        {
            _code.Add((byte)0xCC);
        }

        internal byte[] Build()
        {
            return _code.ToArray();
        }

        private void AppendUInt32(uint value)
        {
            _code.Add((byte)(value & 0xFF));
            _code.Add((byte)((value >> 8) & 0xFF));
            _code.Add((byte)((value >> 16) & 0xFF));
            _code.Add((byte)((value >> 24) & 0xFF));
        }
    }
}
