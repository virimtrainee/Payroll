using System.Collections.Generic;
using System.Globalization;

namespace SalaryManager.App.Helpers;

public record MonthOption(int Number, string Name);

public static class Months
{
    public static List<MonthOption> All { get; } = BuildList();

    private static List<MonthOption> BuildList()
    {
        var list = new List<MonthOption>(12);
        for (int i = 1; i <= 12; i++)
        {
            list.Add(new MonthOption(i, CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i)));
        }
        return list;
    }

    public static List<int> Years(int back = 5, int forward = 1)
    {
        var now = System.DateTime.Now.Year;
        var list = new List<int>();
        for (int y = now - back; y <= now + forward; y++) list.Add(y);
        return list;
    }
}
