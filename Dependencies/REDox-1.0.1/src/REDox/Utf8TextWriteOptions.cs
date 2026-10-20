// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;

namespace REDox;

public readonly record struct Utf8TextWriteOptions
{
    private static readonly byte[] s_newline = Encoding.UTF8.GetBytes(Environment.NewLine);
    private static readonly byte[] s_crlf = "\r\n"u8.ToArray();
    private static readonly byte[] s_lf = "\n"u8.ToArray();

    private readonly MiscFlags _flags;
    private readonly byte _indentSize;

    public bool WriteBom
    {
        get => (_flags & MiscFlags.WriteBom) != 0;
        init
        {
            if (value)
            {
                _flags |= MiscFlags.WriteBom;
            }
            else
            {
                _flags &= ~MiscFlags.WriteBom;
            }
        }
    }

    public bool WriteIndented
    {
        get => (_flags & MiscFlags.WriteIndented) != 0;
        init
        {
            if (value)
            {
                _flags |= MiscFlags.WriteIndented;
            }
            else
            {
                _flags &= ~MiscFlags.WriteIndented;
            }
        }
    }

    public char IndentCharacter
    {
        get
        {
            if ((_flags & MiscFlags.IsTabIndent) != 0)
            {
                return '\t';
            }

            return ' ';
        }
        init
        {
            if (value == '\t')
            {
                _flags |= MiscFlags.IsTabIndent;
            }
            else
            {
                if (value != ' ')
                {
                    throw new ArgumentException("IndentCharacter must be either space or tab.", nameof(value));
                }

                _flags &= ~MiscFlags.IsTabIndent;
            }
        }
    }

    public int IndentSize
    {
        get
        {
            if ((_flags & MiscFlags.HasIndentSize) != 0)
            {
                return _indentSize;
            }

            return 2;
        }
        init
        {
            if (value < 0 || value > 127)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "IndentSize must be between 0 and 127.");
            }

            _indentSize = (byte)value;
            _flags |= MiscFlags.HasIndentSize;
        }
    }

    internal byte[] Utf8NewLine
    {
        get
        {
            if ((_flags & MiscFlags.IsCrLf) != 0)
            {
                return s_crlf;
            }

            if ((_flags & MiscFlags.IsLf) != 0)
            {
                return s_lf;
            }

            return s_newline;
        }
    }

    public string NewLine
    {
        get
        {
            if ((_flags & MiscFlags.IsCrLf) != 0)
            {
                return "\r\n";
            }

            if ((_flags & MiscFlags.IsLf) != 0)
            {
                return "\n";
            }

            return Environment.NewLine;
        }
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value == "\r\n")
            {
                _flags &= ~MiscFlags.IsLf;
                _flags |= MiscFlags.IsCrLf;
            }
            else if (value == "\n")
            {
                _flags &= ~MiscFlags.IsCrLf;
                _flags |= MiscFlags.IsLf;
            }
            else
            {
                throw new ArgumentException("NewLine must be either \\n or \\r\\n.", nameof(value));
            }
        }
    }

    [Flags]
    private enum MiscFlags : byte
    {
        None = 0,
        WriteBom = 1 << 0,
        WriteIndented = 1 << 1,
        IsLf = 1 << 2,
        IsCrLf = 1 << 3,
        IsTabIndent = 1 << 4,
        HasIndentSize = 1 << 5
    }
}