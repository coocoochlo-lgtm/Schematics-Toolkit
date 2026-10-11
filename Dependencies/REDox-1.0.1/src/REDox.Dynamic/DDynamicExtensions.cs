// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Dynamic;

public static class DDynamicExtensions
{
    extension(DElement value)
    {
        public dynamic? AsDynamic()
        {
            return DDynamic.ToValue(value);
        }
    }

    extension(DValue value)
    {
        public dynamic? AsDynamic()
        {
            return DDynamic.ToValue(value.AsElement());
        }
    }

    extension(DContainer value)
    {
        public dynamic AsDynamic()
        {
            return new DDynamic(value);
        }
    }

    extension(IDoxNode value)
    {
        public dynamic? AsDynamic()
        {
            return DDynamic.ToValue(value.AsElement());
        }
    }
}