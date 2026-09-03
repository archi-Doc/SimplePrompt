// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;

namespace SimplePrompt.Internal;

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
        private readonly int _sizeOfInt;
        private readonly Dictionary<string, string>? _extendedStrings;

        internal Database(string term, byte[] data)
        {
            const int MagicLegacyNumber = 0x11A;
            const int Magic32BitNumber = 0x21E;

            if (data.Length < NamesOffset)
            {
                throw new InvalidOperationException("The terminfo header is truncated.");
            }

            this._term = term;
            this._data = data;
            short magic = ReadInt16(data, 0);
            var readAs32Bit =
                magic == MagicLegacyNumber ? false :
                magic == Magic32BitNumber ? true :
                throw new InvalidOperationException();
            this._sizeOfInt = readAs32Bit ? 4 : 2;

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

            var stringsEnd = this.StringsTableOffset + this._stringTableNumBytes;
            if (stringsEnd > data.Length)
            {
                throw new InvalidOperationException("The terminfo sections are truncated.");
            }

            int extendedBeginning = RoundUpToEven(stringsEnd);
            this._extendedStrings = ParseExtendedStrings(data, extendedBeginning, readAs32Bit);
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

            if ((uint)index >= (uint)this._stringSectionNumOffsets)
            {
                return null;
            }

            int tableIndex = ReadInt16(this._data, this.StringOffsetsOffset + (index * 2));
            if (tableIndex < 0 || tableIndex >= this._stringTableNumBytes)
            {
                // Both absent (-1) and canceled (-2) capabilities have no string.
                return null;
            }

            return ReadString(this._data, this.StringsTableOffset + tableIndex, this.StringsTableOffset + this._stringTableNumBytes);
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

            var values = new string?[extendedStringCount];
            var namesStart = extendedStringTableStart;
            for (int i = 0; i < extendedStringCount; i++)
            {
                var relativeOffset = ReadInt16(data, extendedOffsetsStart + (i * 2));
                if (relativeOffset < 0)
                {
                    continue;
                }

                int offset = extendedStringTableStart + relativeOffset;
                var value = ReadString(data, offset, extendedStringTableEnd);
                if (value is null)
                {
                    return null;
                }

                values[i] = value;
                namesStart = Math.Max(namesStart, offset + value.Length + 1);
            }

            var extendedStrings = new Dictionary<string, string>(extendedStringCount);
            var stringNamesOffset = extendedOffsetsStart + ((extendedStringCount + extendedBoolCount + extendedNumberCount) * 2);
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is not { } value)
                {
                    continue;
                }

                var nameOffset = ReadInt16(data, stringNamesOffset + (i * 2));
                if (nameOffset < 0 || ReadString(data, namesStart + nameOffset, extendedStringTableEnd) is not { Length: > 0 } name)
                {
                    return null;
                }

                extendedStrings.TryAdd(name, value);
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

        private static string? ReadString(byte[] buffer, int pos, int end)
        {
            if (pos < 0 || pos >= end || end > buffer.Length)
            {
                return null;
            }

            int length = buffer.AsSpan(pos, end - pos).IndexOf((byte)'\0');
            return length < 0 ? null : Encoding.ASCII.GetString(buffer, pos, length);
        }
    }
}
