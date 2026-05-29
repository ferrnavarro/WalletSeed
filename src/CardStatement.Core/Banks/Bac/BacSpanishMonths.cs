using System;
using System.Collections.Generic;

namespace CardStatement.Core.Banks.Bac;

internal static class BacSpanishMonths
{
    private static readonly Dictionary<string, int> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENE"] = 1,
        ["FEB"] = 2,
        ["MAR"] = 3,
        ["ABR"] = 4,
        ["MAY"] = 5,
        ["JUN"] = 6,
        ["JUL"] = 7,
        ["AGO"] = 8,
        ["SEP"] = 9,
        ["OCT"] = 10,
        ["NOV"] = 11,
        ["DIC"] = 12,
    };

    public static bool TryGet(string token, out int month) => Map.TryGetValue(token, out month);
}
