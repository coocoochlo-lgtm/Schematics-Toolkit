// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace REDox.Serialization;

public abstract class NamingPolicy
{
    public static readonly NamingPolicy CamelCase = new CamelCaseNamingPolicy();

    public static readonly NamingPolicy SnakeCaseLower = new SnakeCaseLowerNamingPolicy();

    public static readonly NamingPolicy SnakeCaseUpper = new SnakeCaseUpperNamingPolicy();

    public static readonly NamingPolicy KebabCaseLower = new KebabCaseLowerNamingPolicy();

    public static readonly NamingPolicy KebabCaseUpper = new KebabCaseUpperNamingPolicy();

    public abstract string ConvertName(string name);

    protected string ConvertSeparatedName(string name, char separator, bool upper)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder();

        var previous = CharCategory.None;
        var pendingSeparator = false;

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (c == separator)
            {
                sb.Append(separator);
                pendingSeparator = false;
                previous = CharCategory.Separator;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSeparator = sb.Length > 0;
                previous = CharCategory.None;
                continue;
            }

            var current =
                char.IsUpper(c) ? CharCategory.Upper :
                char.IsLower(c) ? CharCategory.Lower :
                char.IsDigit(c) ? CharCategory.Digit :
                CharCategory.Other;

            var isBoundary =
                (previous == CharCategory.Lower && current == CharCategory.Upper) ||
                (previous == CharCategory.Digit && current == CharCategory.Upper) ||
                (previous == CharCategory.Upper &&
                 current == CharCategory.Upper &&
                 i + 1 < name.Length &&
                 char.IsLower(name[i + 1]));

            if ((pendingSeparator || isBoundary) &&
                sb.Length > 0 &&
                sb[^1] != separator)
            {
                sb.Append(separator);
            }

            sb.Append(upper
                ? char.ToUpperInvariant(c)
                : char.ToLowerInvariant(c));

            pendingSeparator = false;
            previous = current;
        }

        return sb.ToString();
    }

    private enum CharCategory
    {
        None,
        Upper,
        Lower,
        Digit,
        Separator,
        Other
    }
}

public class CamelCaseNamingPolicy : NamingPolicy
{
    public override string ConvertName(string name)
    {
        if (char.IsUpper(name[0]))
        {
            var chars = name.ToCharArray();

            chars[0] = char.ToLower(chars[0]);

            for (var i = 1; i < chars.Length; i++)
            {
                if (char.IsUpper(chars[i]))
                {
                    if (i + 1 >= chars.Length || char.IsUpper(chars[i + 1]))
                    {
                        chars[i] = char.ToLower(chars[i]);
                    }
                }
                else
                {
                    break;
                }
            }

            name = new string(chars);
        }

        return name;
    }
}

public class KebabCaseLowerNamingPolicy : NamingPolicy
{
    public override string ConvertName(string name)
    {
        return ConvertSeparatedName(name, '-', false);
    }
}

public class SnakeCaseLowerNamingPolicy : NamingPolicy
{
    public override string ConvertName(string name)
    {
        return ConvertSeparatedName(name, '_', false);
    }
}

public class KebabCaseUpperNamingPolicy : NamingPolicy
{
    public override string ConvertName(string name)
    {
        return ConvertSeparatedName(name, '-', true);
    }
}

public class SnakeCaseUpperNamingPolicy : NamingPolicy
{
    public override string ConvertName(string name)
    {
        return ConvertSeparatedName(name, '_', true);
    }
}