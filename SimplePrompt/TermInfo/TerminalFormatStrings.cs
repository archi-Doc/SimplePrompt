// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;

namespace Arc.InputConsole;

#pragma warning disable SA1203 // Constants should appear before fields
#pragma warning disable SA1401 // Fields should be private

internal sealed class TerminalFormatStrings
{
    public readonly Utf16Hashtable<ConsoleKeyInfo> KeyFormatToConsoleKey = new();
    public readonly bool IsRxvtTerm;

    public TerminalFormatStrings(TermInfo.Database? db)
    {
        if (db == null)
        {
            return;
        }

        this.IsRxvtTerm = !string.IsNullOrEmpty(db.Term) && db.Term.Contains("rxvt", StringComparison.OrdinalIgnoreCase);

        this.AddKey(db, TermInfo.WellKnownStrings.KeyF1, ConsoleKey.F1);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF2, ConsoleKey.F2);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF3, ConsoleKey.F3);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF4, ConsoleKey.F4);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF5, ConsoleKey.F5);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF6, ConsoleKey.F6);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF7, ConsoleKey.F7);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF8, ConsoleKey.F8);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF9, ConsoleKey.F9);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF10, ConsoleKey.F10);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF11, ConsoleKey.F11);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF12, ConsoleKey.F12);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF13, ConsoleKey.F13);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF14, ConsoleKey.F14);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF15, ConsoleKey.F15);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF16, ConsoleKey.F16);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF17, ConsoleKey.F17);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF18, ConsoleKey.F18);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF19, ConsoleKey.F19);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF20, ConsoleKey.F20);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF21, ConsoleKey.F21);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF22, ConsoleKey.F22);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF23, ConsoleKey.F23);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyF24, ConsoleKey.F24);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyBackspace, ConsoleKey.Backspace);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyBackTab, ConsoleKey.Tab, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyBegin, ConsoleKey.Home);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyClear, ConsoleKey.Clear);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyDelete, ConsoleKey.Delete);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyDown, ConsoleKey.DownArrow);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyEnd, ConsoleKey.End);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyEnter, ConsoleKey.Enter);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyHelp, ConsoleKey.Help);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyHome, ConsoleKey.Home);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyInsert, ConsoleKey.Insert);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyLeft, ConsoleKey.LeftArrow);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyPageDown, ConsoleKey.PageDown);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyPageUp, ConsoleKey.PageUp);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyPrint, ConsoleKey.Print);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyRight, ConsoleKey.RightArrow);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyScrollForward, ConsoleKey.PageDown, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyScrollReverse, ConsoleKey.PageUp, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeySBegin, ConsoleKey.Home, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeySDelete, ConsoleKey.Delete, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeySHome, ConsoleKey.Home, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeySelect, ConsoleKey.Select);
        this.AddKey(db, TermInfo.WellKnownStrings.KeySLeft, ConsoleKey.LeftArrow, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeySPrint, ConsoleKey.Print, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeySRight, ConsoleKey.RightArrow, shift: true, alt: false, control: false);
        this.AddKey(db, TermInfo.WellKnownStrings.KeyUp, ConsoleKey.UpArrow);
        this.AddPrefixKey(db, "kLFT", ConsoleKey.LeftArrow);
        this.AddPrefixKey(db, "kRIT", ConsoleKey.RightArrow);
        this.AddPrefixKey(db, "kUP", ConsoleKey.UpArrow);
        this.AddPrefixKey(db, "kDN", ConsoleKey.DownArrow);
        this.AddPrefixKey(db, "kDC", ConsoleKey.Delete);
        this.AddPrefixKey(db, "kEND", ConsoleKey.End);
        this.AddPrefixKey(db, "kHOM", ConsoleKey.Home);
        this.AddPrefixKey(db, "kNXT", ConsoleKey.PageDown);
        this.AddPrefixKey(db, "kPRV", ConsoleKey.PageUp);
    }

    private void AddKey(TermInfo.Database db, TermInfo.WellKnownStrings keyId, ConsoleKey key)
    {
        this.AddKey(db, keyId, key, shift: false, alt: false, control: false);
    }

    private void AddKey(TermInfo.Database db, TermInfo.WellKnownStrings keyId, ConsoleKey key, bool shift, bool alt, bool control)
    {
        string? keyFormat = db.GetString(keyId);
        if (!string.IsNullOrEmpty(keyFormat))
        {
            this.KeyFormatToConsoleKey.Add(keyFormat, new ConsoleKeyInfo(key == ConsoleKey.Enter ? '\r' : '\0', key, shift, alt, control));
        }
    }

    private void AddPrefixKey(TermInfo.Database db, string extendedNamePrefix, ConsoleKey key)
    {
        if (db.HasExtendedStrings)
        {
            this.AddKey(db, extendedNamePrefix + "3", key, shift: false, alt: true, control: false);
            this.AddKey(db, extendedNamePrefix + "4", key, shift: true, alt: true, control: false);
            this.AddKey(db, extendedNamePrefix + "5", key, shift: false, alt: false, control: true);
            this.AddKey(db, extendedNamePrefix + "6", key, shift: true, alt: false, control: true);
            this.AddKey(db, extendedNamePrefix + "7", key, shift: false, alt: false, control: true);
        }
    }

    private void AddKey(TermInfo.Database db, string extendedName, ConsoleKey key, bool shift, bool alt, bool control)
    {
        string? keyFormat = db.GetExtendedString(extendedName);
        if (!string.IsNullOrEmpty(keyFormat))
        {
            this.KeyFormatToConsoleKey.Add(keyFormat, new ConsoleKeyInfo('\0', key, shift, alt, control));
        }
    }
}
