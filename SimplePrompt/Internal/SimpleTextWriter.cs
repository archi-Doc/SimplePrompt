// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SimplePrompt.Internal;

/// <summary>
/// A <see cref="TextWriter"/> installed as <see cref="Console.Out"/> so that Console output goes through <see cref="SimpleConsole"/>.
/// </summary>
internal sealed class SimpleTextWriter : TextWriter
{
    public SimpleConsole SimpleConsole { get; }

    public TextWriter UnderlyingTextWriter { get; }

    public SimpleTextWriter(SimpleConsole simpleConsole, TextWriter inner)
    {
        this.SimpleConsole = simpleConsole;
        this.UnderlyingTextWriter = inner;
    }

    public override Encoding Encoding => System.Text.Encoding.UTF8;

    public override IFormatProvider FormatProvider => this.UnderlyingTextWriter.FormatProvider;

    public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        => this.SimpleConsole.WriteSpan(string.Format(this.FormatProvider, format, arg0), false);

    public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        => this.SimpleConsole.WriteSpan(string.Format(this.FormatProvider, format, arg0), true);

    public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        => this.SimpleConsole.WriteSpan(string.Format(this.FormatProvider, format, arg0, arg1), false);

    public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        => this.SimpleConsole.WriteSpan(string.Format(this.FormatProvider, format, arg0, arg1), true);

    public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        => this.SimpleConsole.WriteSpan(string.Format(this.FormatProvider, format, arg0, arg1, arg2), false);

    public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        => this.SimpleConsole.WriteSpan(string.Format(this.FormatProvider, format, arg0, arg1, arg2), true);

    public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
        => this.SimpleConsole.WriteSpan(string.Format(this.FormatProvider, format, arg), false);

    public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
        => this.SimpleConsole.WriteSpan(string.Format(this.FormatProvider, format, arg), true);

    public override void Write(char[] buffer, int index, int count)
        => this.SimpleConsole.WriteSpan(buffer.AsSpan(index, count), false);

    public override void WriteLine(char[] buffer, int index, int count)
        => this.SimpleConsole.WriteSpan(buffer.AsSpan(index, count), true);

    public override void Write(bool value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(bool value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(char value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(char value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(char[]? value)
        => this.SimpleConsole.WriteSpan(value, false);

    public override void WriteLine(char[]? value)
        => this.SimpleConsole.WriteSpan(value, true);

    public override void Write(decimal value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(decimal value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(double value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(double value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(float value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(float value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(int value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(int value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(uint value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(uint value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(long value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(long value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(ulong value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(ulong value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(string? value)
        => this.SimpleConsole.Write(value);

    public override void WriteLine(string? value)
        => this.SimpleConsole.WriteLine(value);

    public override void Write(object? value)
    {
        if (value is not null)
        {
            this.WriteObject(value, false);
        }
    }

    public override void WriteLine(object? value)
    {
        if (value is null)
        {
            this.SimpleConsole.WriteLine();
        }
        else
        {
            this.WriteObject(value, true);
        }
    }

    public override void Write(ReadOnlySpan<char> value)
        => this.SimpleConsole.WriteSpan(value, false);

    public override void WriteLine(ReadOnlySpan<char> value)
        => this.SimpleConsole.WriteSpan(value, true);

    public override void Write(StringBuilder? value)
    {
        if (value is not null)
        {
            this.WriteBuilder(value, false);
        }
    }

    public override void WriteLine(StringBuilder? value)
    {
        if (value is null)
        {
            this.SimpleConsole.WriteLine();
        }
        else
        {
            this.WriteBuilder(value, true);
        }
    }

    public override void WriteLine()
        => this.SimpleConsole.WriteLine();

    public override void Flush()
        => this.UnderlyingTextWriter.Flush();

    private void WriteObject(object value, bool newLine)
    {
        if (value is ISpanFormattable spanFormattable)
        {
            Span<char> buffer = stackalloc char[128];
            if (spanFormattable.TryFormat(buffer, out var written, default, this.FormatProvider))
            {
                this.SimpleConsole.WriteSpan(buffer.Slice(0, written), newLine);
                return;
            }
        }

        var text = value is IFormattable formattable
            ? formattable.ToString(default, this.FormatProvider)
            : value.ToString();
        this.SimpleConsole.WriteSpan(text, newLine);
    }

    private void WriteBuilder(StringBuilder value, bool newLine)
    {
        char[]? rented = null;
        Span<char> buffer = value.Length <= 256
            ? stackalloc char[256]
            : rented = ArrayPool<char>.Shared.Rent(value.Length);
        try
        {
            value.CopyTo(0, buffer, value.Length);
            this.SimpleConsole.WriteSpan(buffer.Slice(0, value.Length), newLine);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }
}
