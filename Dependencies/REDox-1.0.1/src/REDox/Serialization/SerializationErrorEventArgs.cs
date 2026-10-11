// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

class SerializationErrorEventArgs : EventArgs
{
    public SerializationErrorEventArgs(SerializationException error)
    {
        Error = error;
    }

    public SerializationException Error { get; }

    public bool Handled { get; set; }
}