// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;

namespace Arc.InputConsole;

#pragma warning disable SA1203 // Constants should appear before fields

internal static partial class TermInfo
{
    internal sealed class Database
    {
        private readonly string _term;
        private readonly byte[] _data;
        private readonly int _nameSectionNumBytes;
        private readonly int _boolSectionNumBytes;
        private readonly int _numberSectionNumInts;
        private readonly int _stringSectionNumOffsets;
        private readonly int _stringTableNumBytes;
        private readonly bool _readAs32Bit;
        private readonly int _sizeOfInt;
        private readonly Dictionary<string, string>? _extendedStrings;

        internal Database(string term, byte[] data)
        {
            this._term = term;
            this._data = data;

            const int MagicLegacyNumber = 0x11A;
            const int Magic32BitNumber = 0x21E;
            short magic = ReadInt16(data, 0);
            this._readAs32Bit =
                magic == MagicLegacyNumber ? false :
                magic == Magic32BitNumber ? true :
                throw new InvalidOperationException();
            this._sizeOfInt = this._readAs32Bit ? 4 : 2;

            this._nameSectionNumBytes = ReadInt16(data, 2);
            this._boolSectionNumBytes = ReadInt16(data, 4);
            this._numberSectionNumInts = ReadInt16(data, 6);
            this._stringSectionNumOffsets = ReadInt16(data, 8);
            this._stringTableNumBytes = ReadInt16(data, 10);
            if (this._nameSectionNumBytes < 0 ||
                this._boolSectionNumBytes < 0 ||
                this._numberSectionNumInts < 0 ||
                this._stringSectionNumOffsets < 0 ||
                this._stringTableNumBytes < 0)
            {
                throw new InvalidOperationException();
            }

            int extendedBeginning = RoundUpToEven(this.StringsTableOffset + this._stringTableNumBytes);
            this._extendedStrings = ParseExtendedStrings(data, extendedBeginning, this._readAs32Bit);
        }

        public string Term => this._term;

        internal bool HasExtendedStrings => this._extendedStrings is not null;

        private const int NamesOffset = 12;

        private int BooleansOffset => NamesOffset + this._nameSectionNumBytes;

        private int NumbersOffset => RoundUpToEven(this.BooleansOffset + this._boolSectionNumBytes);

        private int StringOffsetsOffset => this.NumbersOffset + (this._numberSectionNumInts * this._sizeOfInt);

        private int StringsTableOffset => this.StringOffsetsOffset + (this._stringSectionNumOffsets * 2);

        public string? GetString(WellKnownStrings stringTableIndex)
        {
            int index = (int)stringTableIndex;
            Debug.Assert(index >= 0);

            if (index >= this._stringSectionNumOffsets)
            {
                return null;
            }

            int tableIndex = ReadInt16(this._data, this.StringOffsetsOffset + (index * 2));
            if (tableIndex == -1)
            {
                return null;
            }

            return ReadString(this._data, this.StringsTableOffset + tableIndex);
        }

        public string? GetExtendedString(string name)
        {
            Debug.Assert(name != null);

            string? value;
            return this._extendedStrings is not null && this._extendedStrings.TryGetValue(name, out value) ? value : null;
        }

        private static Dictionary<string, string>? ParseExtendedStrings(byte[] data, int extendedBeginning, bool readAs32Bit)
        {
            const int ExtendedHeaderSize = 10;
            int sizeOfIntValuesInBytes = readAs32Bit ? 4 : 2;
            if (extendedBeginning + ExtendedHeaderSize >= data.Length)
            {
                return null;
            }

            int extendedBoolCount = ReadInt16(data, extendedBeginning);
            int extendedNumberCount = ReadInt16(data, extendedBeginning + (2 * 1));
            int extendedStringCount = ReadInt16(data, extendedBeginning + (2 * 2));
            int extendedStringNumOffsets = ReadInt16(data, extendedBeginning + (2 * 3));
            int extendedStringTableByteSize = ReadInt16(data, extendedBeginning + (2 * 4));
            if (extendedBoolCount < 0 ||
                extendedNumberCount < 0 ||
                extendedStringCount < 0 ||
                extendedStringNumOffsets < 0 ||
                extendedStringTableByteSize < 0)
            {
                return null;
            }

            int extendedOffsetsStart = extendedBeginning + ExtendedHeaderSize +
                RoundUpToEven(extendedBoolCount) + (extendedNumberCount * sizeOfIntValuesInBytes);

            int extendedStringTableStart = extendedOffsetsStart + (extendedStringCount * 2) + ((extendedBoolCount + extendedNumberCount + extendedStringCount) * 2);
            int extendedStringTableEnd = extendedStringTableStart + extendedStringTableByteSize;
            if (extendedStringTableEnd > data.Length)
            {
                return null;
            }

            var values = new List<string>(extendedStringCount);
            int lastEnd = 0;
            for (int i = 0; i < extendedStringCount; i++)
            {
                int offset = extendedStringTableStart + ReadInt16(data, extendedOffsetsStart + (i * 2));
                if (offset < 0 || offset >= data.Length)
                {
                    return null;
                }

                int end = FindNullTerminator(data, offset);
                values.Add(Encoding.ASCII.GetString(data, offset, end - offset));

                lastEnd = Math.Max(end, lastEnd);
            }

            var names = new List<string>(extendedBoolCount + extendedNumberCount + extendedStringCount);
            for (int pos = lastEnd + 1; pos < extendedStringTableEnd; pos++)
            {
                int end = FindNullTerminator(data, pos);
                names.Add(Encoding.ASCII.GetString(data, pos, end - pos));
                pos = end;
            }

            var extendedStrings = new Dictionary<string, string>(extendedStringCount);
            for (int iName = extendedBoolCount + extendedNumberCount, iValue = 0;
                 iName < names.Count && iValue < values.Count;
                 iName++, iValue++)
            {
                extendedStrings.Add(names[iName], values[iValue]);
            }

            return extendedStrings;
        }

        private static int RoundUpToEven(int i)
        {
            return i % 2 == 1 ? i + 1 : i;
        }

        private static short ReadInt16(byte[] buffer, int pos)
        {
            return unchecked((short)((((int)buffer[pos + 1]) << 8) | ((int)buffer[pos] & 0xff)));
        }

        private static string ReadString(byte[] buffer, int pos)
        {
            int end = FindNullTerminator(buffer, pos);
            return Encoding.ASCII.GetString(buffer, pos, end - pos);
        }

        private static int FindNullTerminator(byte[] buffer, int pos)
        {
            int i = buffer.AsSpan(pos).IndexOf((byte)'\0');
            return i >= 0 ? pos + i : buffer.Length;
        }
    }
}
