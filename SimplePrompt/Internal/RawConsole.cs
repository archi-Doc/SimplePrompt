// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Arc;

namespace SimplePrompt;

internal sealed class RawConsole
{
    private const int BufferCapacity = 1024;
    private const int MinimalSequenceLength = 3;
    private const int SequencePrefixLength = 2; // ^[[ ("^[" stands for Escape)
    private const char Escape = '\e';
    private const char Delete = '\u007F';
    private const char VtSequenceEndTag = '~';
    private const char ModifierSeparator = ';';

    private readonly InputConsole inputConsole;
    private readonly Encoding encoding;
    private readonly TermInfo.Database? db;
    private readonly TerminalFormatStrings terminalFormatStrings;

    private readonly Lock bufferLock = new();
    private readonly byte[] bytes = new byte[BufferCapacity];
    private readonly char[] chars = new char[BufferCapacity];
    private int bytesLength = 0;
    private int charsStartIndex = 0;
    private int charsEndIndex = 0;

    private SafeHandle? handle;
    private bool useStdin;
    private byte posixDisableValue;
    private byte veraseCharacter;

    public Span<char> CharsSpan => this.chars.AsSpan(this.charsStartIndex, this.charsEndIndex - this.charsStartIndex);

    public bool IsBytesEmpty => this.bytesLength == 0;

    public bool IsCharsEmpty => this.charsStartIndex >= this.charsEndIndex;

    public RawConsole(InputConsole inputConsole, CancellationToken cancellationToken = default)
    {
        this.inputConsole = inputConsole;
        this.encoding = Encoding.UTF8;

        try
        {
            this.InitializeStdin();
            this.db = TermInfo.DatabaseFactory.ReadActiveDatabase();
            Console.WriteLine("Stdin");
        }
        catch
        {
        }

        this.terminalFormatStrings = new(this.db);
    }

    public unsafe bool TryRead(out ConsoleKeyInfo keyInfo)
    {
        try
        {
            if (this.useStdin)
            {// Stdin
                if (this.TryConsumeBuffer(out keyInfo))
                {
                    return true;
                }

                if (!Interop.Sys.StdinReady())
                {// No key available
                    keyInfo = default;
                    return false;
                }

                using (this.bufferLock.EnterScope())
                {
                    if (this.TryConsumeBufferInternal(out keyInfo))
                    {
                        return true;
                    }

                    Interop.Sys.InitializeConsoleBeforeRead();
                    try
                    {
                        var span = this.bytes.AsSpan(this.bytesLength, this.bytes.Length - this.bytesLength);
                        fixed (byte* buffer = span)
                        {
                            var readLength = Interop.Sys.ReadStdin(buffer, span.Length);
                            this.bytesLength += readLength;
                        }

                        var validLength = BaseHelper.GetValidUtf8Length(this.bytes.AsSpan(0, this.bytesLength));

                        Debug.Assert(this.IsCharsEmpty);
                        this.charsStartIndex = 0;
                        this.charsEndIndex = this.encoding.GetChars(this.bytes.AsSpan(0, validLength), this.chars.AsSpan());
                        this.bytesLength -= validLength;
                        if (validLength < this.bytesLength)
                        {// Move remaining bytes to the front
                            this.bytes.AsSpan(validLength, this.bytesLength).CopyTo(this.bytes.AsSpan());
                        }
                    }
                    finally
                    {
                        Interop.Sys.UninitializeConsoleAfterRead();
                    }

                    return this.TryConsumeBufferInternal(out keyInfo);
                }
            }
            else
            {// Console.ReadKey
                if (!Console.KeyAvailable)
                {// No key available
                    keyInfo = default;
                    return false;
                }

                keyInfo = Console.ReadKey(intercept: true);
                return true;
            }
        }
        catch
        {
            keyInfo = default;
            return false;
        }
    }

    public unsafe void WriteInternal(ReadOnlySpan<char> data)
    {
        try
        {
            if (this.handle is not null)
            {
                var length = Encoding.UTF8.GetBytes(data, this.inputConsole.Utf8Buffer);
                fixed (byte* p = this.inputConsole.Utf8Buffer)
                {
                    _ = Interop.Sys.Write(this.handle, p, length);
                }
            }
            else
            {
                Console.Out.Write(data);
            }
        }
        catch
        {
        }
    }

    private bool TryConsumeBuffer(out ConsoleKeyInfo keyInfo)
    {
        if (this.IsCharsEmpty)
        {
            keyInfo = default;
            return false;
        }

        using (this.bufferLock.EnterScope())
        {
            return this.TryConsumeBufferInternal(out keyInfo);
        }
    }

    private bool TryConsumeBufferInternal(out ConsoleKeyInfo keyInfo)
    {
        if (this.IsCharsEmpty)
        {
            keyInfo = default;
            return false;
        }

        var span = this.CharsSpan;
        if (span[0] != this.posixDisableValue && span[0] == this.veraseCharacter)
        {
            keyInfo = new(span[0], ConsoleKey.Backspace, false, false, false);
            this.charsStartIndex++;
            return true;
        }
        else if (span.Length >= MinimalSequenceLength + 1 && span[0] == Escape && span[1] == Escape)
        {
            this.charsStartIndex++;
            if (this.TryParseTerminalInputSequence(out var parsed))
            {
                keyInfo = new(parsed.KeyChar, parsed.Key, (parsed.Modifiers & ConsoleModifiers.Shift) != 0, alt: true, (parsed.Modifiers & ConsoleModifiers.Control) != 0);
                return true;
            }

            this.charsStartIndex--;
        }
        else if (span.Length >= MinimalSequenceLength && this.TryParseTerminalInputSequence(out keyInfo))
        {
            return true;
        }

        if (span.Length == 2 && span[0] == Escape && span[1] != Escape)
        {
            this.charsStartIndex++;
            keyInfo = this.ParseFromSingleChar(span[0], isAlt: true);
            this.charsStartIndex++;
            return true;
        }

        keyInfo = this.ParseFromSingleChar(span[0], isAlt: false);
        this.charsStartIndex++;
        return true;
    }

    private ConsoleKeyInfo ParseFromSingleChar(char single, bool isAlt)
    {
        bool isShift = false, isCtrl = false;
        char keyChar = single;

        ConsoleKey key = single switch
        {
            '\b' => ConsoleKey.Backspace,
            '\t' => ConsoleKey.Tab,
            '\r' or '\n' => ConsoleKey.Enter,
            ' ' => ConsoleKey.Spacebar,
            Escape => ConsoleKey.Escape,
            Delete => ConsoleKey.Backspace,
            '*' => ConsoleKey.Multiply,
            '/' => ConsoleKey.Divide,
            '-' => ConsoleKey.Subtract,
            '+' => ConsoleKey.Add,
            '=' => default,
            '!' or '@' or '#' or '$' or '%' or '^' or '&' or '&' or '*' or '(' or ')' => default,
            ',' => ConsoleKey.OemComma,
            '.' => ConsoleKey.OemPeriod,
            _ when char.IsAsciiLetterLower(single) => ConsoleKey.A + single - 'a',
            _ when char.IsAsciiLetterUpper(single) => UppercaseCharacter(single, out isShift),
            _ when char.IsAsciiDigit(single) => ConsoleKey.D0 + single - '0',
            _ when char.IsBetween(single, (char)1, (char)26) => ControlAndLetterPressed(single, isAlt, out keyChar, out isCtrl),
            _ when char.IsBetween(single, (char)28, (char)31) => ControlAndDigitPressed(single, out keyChar, out isCtrl),
            '\u0000' => ControlAndDigitPressed(single, out keyChar, out isCtrl),
            _ => default,
        };

        if (single is '\b' or '\n')
        {
            isCtrl = true;
        }

        if (isAlt)
        {
            isAlt = key != default;
        }

        return new ConsoleKeyInfo(keyChar, key, isShift, isAlt, isCtrl);

        static ConsoleKey UppercaseCharacter(char single, out bool isShift)
        {
            isShift = true;
            return ConsoleKey.A + single - 'A';
        }

        static ConsoleKey ControlAndLetterPressed(char single, bool isAlt, out char keyChar, out bool isCtrl)
        {
            Debug.Assert(single != 'b' && single != '\t' && single != '\n' && single != '\r');

            isCtrl = true;
            keyChar = isAlt ? default : single;
            return ConsoleKey.A + single - 1;
        }

        static ConsoleKey ControlAndDigitPressed(char single, out char keyChar, out bool isCtrl)
        {
            Debug.Assert(single == default || char.IsBetween(single, (char)28, (char)31));

            isCtrl = true;
            keyChar = default;
            return single switch
            {
                '\u0000' => ConsoleKey.D2,
                _ => ConsoleKey.D4 + single - 28,
            };
        }
    }

    private bool TryParseTerminalInputSequence(out ConsoleKeyInfo parsed)
    {
        var input = this.CharsSpan;
        parsed = default;

        // sequences start with either "^[[" or "^[O". "^[" stands for Escape (27).
        if (input.Length < MinimalSequenceLength || input[0] != Escape || (input[1] != '[' && input[1] != 'O'))
        {
            return false;
        }

        var terminfoDb = this.terminalFormatStrings.KeyFormatToConsoleKey;
        ConsoleModifiers modifiers = ConsoleModifiers.None;
        ConsoleKey key;

        // Is it a three character sequence? (examples: '^[[H' (Home), '^[OP' (F1))
        if (input[1] == 'O' || char.IsAsciiLetter(input[2]) || input.Length == MinimalSequenceLength)
        {
            if (!terminfoDb.TryGetValue(this.chars.AsSpan(this.charsStartIndex, MinimalSequenceLength), out parsed))
            {
                // All terminals which use "^[O{letter}" escape sequences don't define conflicting mappings.
                // Example: ^[OH either means Home or simply is not used by given terminal.
                // But with "^[[{character}" sequences, there are conflicts between rxvt and SCO.
                // Example: "^[[a" is Shift+UpArrow for rxvt and Shift+F3 for SCO.
                (key, modifiers) = input[1] == 'O' || this.terminalFormatStrings.IsRxvtTerm
                    ? MapKeyIdOXterm(input[2], this.terminalFormatStrings.IsRxvtTerm)
                    : MapSCO(input[2]);

                if (key == default)
                {
                    return false; // it was not a known sequence
                }

                char keyChar = key switch
                {
                    ConsoleKey.Enter => '\r', // "^[OM" should produce new line character (was not previously mapped this way)
                    ConsoleKey.Add => '+',
                    ConsoleKey.Subtract => '-',
                    ConsoleKey.Divide => '/',
                    ConsoleKey.Multiply => '*',
                    _ => default,
                };
                parsed = Create(keyChar, key, modifiers);
            }

            this.charsStartIndex += MinimalSequenceLength;
            return true;
        }

        // Is it a four character sequence used by Linux Console or PuTTy configured to emulate it? (examples: '^[[[A' (F1), '^[[[B' (F2))
        if (input[1] == '[' && input[2] == '[' && char.IsBetween(input[3], 'A', 'E'))
        {
            if (!terminfoDb.TryGetValue(this.chars.AsSpan(this.charsStartIndex, 4), out parsed))
            {
                parsed = new ConsoleKeyInfo(default, ConsoleKey.F1 + input[3] - 'A', false, false, false);
            }

            this.charsStartIndex += 4;
            return true;
        }

        // If sequence does not start with a letter, it must start with one or two digits that represent the Sequence Number
        int digitCount = !char.IsBetween(input[SequencePrefixLength], '1', '9') // not using IsAsciiDigit as 0 is invalid
            ? 0
            : char.IsDigit(input[SequencePrefixLength + 1]) ? 2 : 1;

        if (digitCount == 0 || SequencePrefixLength + digitCount >= input.Length)
        {
            parsed = default;
            return false;
        }

        if (IsSequenceEndTag(input[SequencePrefixLength + digitCount]))
        {
            // it's a VT Sequence like ^[[11~ or rxvt like ^[[11^
            int sequenceLength = SequencePrefixLength + digitCount + 1;
            if (!terminfoDb.TryGetValue(this.chars.AsSpan(this.charsStartIndex, sequenceLength), out parsed))
            {
                key = MapEscapeSequenceNumber(byte.Parse(input.Slice(SequencePrefixLength, digitCount)));
                if (key == default)
                {
                    return false; // it was not a known sequence
                }

                if (IsRxvtModifier(input[SequencePrefixLength + digitCount]))
                {
                    modifiers = MapRxvtModifiers(input[SequencePrefixLength + digitCount]);
                }

                parsed = Create(default, key, modifiers);
            }

            this.charsStartIndex += sequenceLength;
            return true;
        }

        // If Sequence Number is not followed by the VT Seqence End Tag,
        // it can be followed only by a Modifier Separator, Modifier (2-8) and Key ID or VT Sequence End Tag.
        if (input[SequencePrefixLength + digitCount] is not ModifierSeparator
            || SequencePrefixLength + digitCount + 2 >= input.Length
            || !char.IsBetween(input[SequencePrefixLength + digitCount + 1], '2', '8')
            || (!char.IsAsciiLetterUpper(input[SequencePrefixLength + digitCount + 2]) && input[SequencePrefixLength + digitCount + 2] is not VtSequenceEndTag))
        {
            return false;
        }

        modifiers = MapXtermModifiers(input[SequencePrefixLength + digitCount + 1]);

        key = input[SequencePrefixLength + digitCount + 2] is VtSequenceEndTag
            ? MapEscapeSequenceNumber(byte.Parse(input.Slice(SequencePrefixLength, digitCount)))
            : MapKeyIdOXterm(input[SequencePrefixLength + digitCount + 2], this.terminalFormatStrings.IsRxvtTerm).Key;

        if (key == default)
        {
            return false;
        }

        this.charsStartIndex += SequencePrefixLength + digitCount + 3; // 3 stands for separator, modifier and end tag or id
        parsed = Create(default, key, modifiers);
        return true;

        // maps "^[O{character}" for all Terminals and "^[[{character}" for rxvt Terminals
        static (ConsoleKey Key, ConsoleModifiers Modifiers) MapKeyIdOXterm(char character, bool isRxvt)
            => character switch
            {
                'A' or 'x' => (ConsoleKey.UpArrow, 0), // lowercase used by rxvt
                'a' => (ConsoleKey.UpArrow, ConsoleModifiers.Shift), // rxvt
                'B' or 'r' => (ConsoleKey.DownArrow, 0), // lowercase used by rxv
                'b' => (ConsoleKey.DownArrow, ConsoleModifiers.Shift), // used by rxvt
                'C' or 'v' => (ConsoleKey.RightArrow, 0), // lowercase used by rxv
                'c' => (ConsoleKey.RightArrow, ConsoleModifiers.Shift), // used by rxvt
                'D' or 't' => (ConsoleKey.LeftArrow, 0), // lowercase used by rxv
                'd' => (ConsoleKey.LeftArrow, ConsoleModifiers.Shift), // used by rxvt
                'E' => (ConsoleKey.NoName, 0), // ^[OE maps to Begin, but we don't have such Key. To reproduce press Num5.
                'F' or 'q' => (ConsoleKey.End, 0),
                'H' => (ConsoleKey.Home, 0),
                'j' => (ConsoleKey.Multiply, 0), // used by both xterm and rxvt
                'k' => (ConsoleKey.Add, 0), // used by both xterm and rxvt
                'm' => (ConsoleKey.Subtract, 0), // used by both xterm and rxvt
                'M' => (ConsoleKey.Enter, 0), // used by xterm, rxvt (they have it Terminfo) and tmux (no record in Terminfo)
                'n' => (ConsoleKey.Delete, 0), // rxvt
                'o' => (ConsoleKey.Divide, 0), // used by both xterm and rxvt
                'P' => (ConsoleKey.F1, 0),
                'p' => (ConsoleKey.Insert, 0), // rxvt
                'Q' => (ConsoleKey.F2, 0),
                'R' => (ConsoleKey.F3, 0),
                'S' => (ConsoleKey.F4, 0),
                's' => (ConsoleKey.PageDown, 0), // rxvt
                'T' => (ConsoleKey.F5, 0), // VT 100+
                'U' => (ConsoleKey.F6, 0), // VT 100+
                'u' => (ConsoleKey.NoName, 0), // it should be Begin, but we don't have such (press Num5 in rxvt to reproduce)
                'V' => (ConsoleKey.F7, 0), // VT 100+
                'W' => (ConsoleKey.F8, 0), // VT 100+
                'w' when isRxvt => (ConsoleKey.Home, 0),
                'w' when !isRxvt => (ConsoleKey.End, 0),
                'X' => (ConsoleKey.F9, 0), // VT 100+
                'Y' => (ConsoleKey.F10, 0), // VT 100+
                'y' => (ConsoleKey.PageUp, 0), // rxvt
                'Z' => (ConsoleKey.F11, 0), // VT 100+
                '[' => (ConsoleKey.F12, 0), // VT 100+
                _ => default,
            };

        // maps "^[[{character}" for SCO terminals, based on https://vt100.net/docs/vt510-rm/chapter6.html
        static (ConsoleKey Key, ConsoleModifiers Modifiers) MapSCO(char character)
            => character switch
            {
                'A' => (ConsoleKey.UpArrow, 0),
                'B' => (ConsoleKey.DownArrow, 0),
                'C' => (ConsoleKey.RightArrow, 0),
                'D' => (ConsoleKey.LeftArrow, 0),
                'F' => (ConsoleKey.End, 0),
                'G' => (ConsoleKey.PageDown, 0),
                'H' => (ConsoleKey.Home, 0),
                'I' => (ConsoleKey.PageUp, 0),
                _ when char.IsBetween(character, 'M', 'X') => (ConsoleKey.F1 + character - 'M', 0),
                _ when char.IsBetween(character, 'Y', 'Z') => (ConsoleKey.F1 + character - 'Y', ConsoleModifiers.Shift),
                _ when char.IsBetween(character, 'a', 'j') => (ConsoleKey.F3 + character - 'a', ConsoleModifiers.Shift),
                _ when char.IsBetween(character, 'k', 'v') => (ConsoleKey.F1 + character - 'k', ConsoleModifiers.Control),
                _ when char.IsBetween(character, 'w', 'z') => (ConsoleKey.F1 + character - 'w', ConsoleModifiers.Control | ConsoleModifiers.Shift),
                '@' => (ConsoleKey.F5, ConsoleModifiers.Control | ConsoleModifiers.Shift),
                '[' => (ConsoleKey.F6, ConsoleModifiers.Control | ConsoleModifiers.Shift),
                '<' or '\\' => (ConsoleKey.F7, ConsoleModifiers.Control | ConsoleModifiers.Shift), // the Spec says <, PuTTy uses \.
                ']' => (ConsoleKey.F8, ConsoleModifiers.Control | ConsoleModifiers.Shift),
                '^' => (ConsoleKey.F9, ConsoleModifiers.Control | ConsoleModifiers.Shift),
                '_' => (ConsoleKey.F10, ConsoleModifiers.Control | ConsoleModifiers.Shift),
                '`' => (ConsoleKey.F11, ConsoleModifiers.Control | ConsoleModifiers.Shift),
                '{' => (ConsoleKey.F12, ConsoleModifiers.Control | ConsoleModifiers.Shift),
                _ => default,
            };

        // based on https://en.wikipedia.org/wiki/ANSI_escape_code#Fe_Escape_sequences
        static ConsoleKey MapEscapeSequenceNumber(byte number)
            => number switch
            {
                1 or 7 => ConsoleKey.Home,
                2 => ConsoleKey.Insert,
                3 => ConsoleKey.Delete,
                4 or 8 => ConsoleKey.End,
                5 => ConsoleKey.PageUp,
                6 => ConsoleKey.PageDown,
                // Limitation: 10 is mapped to F0, ConsoleKey does not define it so it's not supported.
                11 => ConsoleKey.F1,
                12 => ConsoleKey.F2,
                13 => ConsoleKey.F3,
                14 => ConsoleKey.F4,
                15 => ConsoleKey.F5,
                17 => ConsoleKey.F6,
                18 => ConsoleKey.F7,
                19 => ConsoleKey.F8,
                20 => ConsoleKey.F9,
                21 => ConsoleKey.F10,
                23 => ConsoleKey.F11,
                24 => ConsoleKey.F12,
                25 => ConsoleKey.F13,
                26 => ConsoleKey.F14,
                28 => ConsoleKey.F15,
                29 => ConsoleKey.F16,
                31 => ConsoleKey.F17,
                32 => ConsoleKey.F18,
                33 => ConsoleKey.F19,
                34 => ConsoleKey.F20,
                // 9, 16, 22, 27, 30 and 35 have no mapping
                _ => default,
            };

        // based on https://www.xfree86.org/current/ctlseqs.html
        static ConsoleModifiers MapXtermModifiers(char modifier)
            => modifier switch
            {
                '2' => ConsoleModifiers.Shift,
                '3' => ConsoleModifiers.Alt,
                '4' => ConsoleModifiers.Shift | ConsoleModifiers.Alt,
                '5' => ConsoleModifiers.Control,
                '6' => ConsoleModifiers.Shift | ConsoleModifiers.Control,
                '7' => ConsoleModifiers.Alt | ConsoleModifiers.Control,
                '8' => ConsoleModifiers.Shift | ConsoleModifiers.Alt | ConsoleModifiers.Control,
                _ => default,
            };

        static bool IsSequenceEndTag(char character) => character is VtSequenceEndTag || IsRxvtModifier(character);

        static bool IsRxvtModifier(char character) => MapRxvtModifiers(character) != default;

        static ConsoleModifiers MapRxvtModifiers(char modifier)
            => modifier switch
            {
                '^' => ConsoleModifiers.Control,
                '$' => ConsoleModifiers.Shift,
                '@' => ConsoleModifiers.Control | ConsoleModifiers.Shift,
                _ => default,
            };

        static ConsoleKeyInfo Create(char keyChar, ConsoleKey key, ConsoleModifiers modifiers)
            => new(keyChar, key, (modifiers & ConsoleModifiers.Shift) != 0, (modifiers & ConsoleModifiers.Alt) != 0, (modifiers & ConsoleModifiers.Control) != 0);
    }

    private void InitializeStdin()
    {
        this.handle = Interop.Sys.Dup(Interop.FileDescriptors.STDIN_FILENO);

        Span<Interop.ControlCharacterNames> controlCharacterNames =
        [
            Interop.ControlCharacterNames.VERASE,
            Interop.ControlCharacterNames.VEOL,
            Interop.ControlCharacterNames.VEOL2,
            Interop.ControlCharacterNames.VEOF,
        ];

        Span<byte> controlCharacterValues = stackalloc byte[controlCharacterNames.Length];
        Interop.Sys.GetControlCharacters(controlCharacterNames, controlCharacterValues, controlCharacterNames.Length, out var posixDisableValue);
        this.posixDisableValue = posixDisableValue;
        this.veraseCharacter = controlCharacterValues[0];

        Interop.Sys.InitializeConsoleBeforeRead();
        Interop.Sys.UninitializeConsoleAfterRead();

        this.useStdin = true;
    }
}
