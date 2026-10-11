// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Serialization;

public interface ITextEncoder
{
    TextEscapeMask EscapeMask { get; }
}

public interface ITextEncoder<out TSelf> : ITextEncoder
    where TSelf : class, ITextEncoder<TSelf>
{
    static abstract TSelf Create(TextEncoderPolicy policy);
}