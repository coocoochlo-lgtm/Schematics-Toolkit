// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using REDox.Serialization.Metadata;

namespace REDox.Serialization;

sealed class ReferenceLoopException : Exception
{
}

public sealed class SerializationException : Exception
{
    internal SerializationException(SerializationError error, Exception? innerException = null) : base(null,
        innerException)
    {
        Error = error;
    }

    [DataIgnore] public SerializationError Error { get; }

    [DataIgnore] public object? Member { get; init; }

    [DataIgnore] public object? Target { get; init; }

    [DataIgnore] public DataContract? Contract { get; init; }

    public override string Message
    {
        get
        {
            var ss = new StringBuilder();
            ss.Append(Error.ToString());

            if (Member != null)
            {
                ss.AppendFormat(" Member:{0}", Member);
            }

            if (Target != null)
            {
                ss.AppendFormat(" Target:{0}", Target.GetType().FullName);
            }

            if (Contract != null)
            {
                ss.AppendFormat(" Type:{0}", Contract.Type);
            }

            return ss.ToString();
        }
    }
}