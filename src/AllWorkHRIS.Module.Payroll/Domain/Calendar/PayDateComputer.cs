namespace AllWorkHRIS.Module.Payroll.Domain.Calendar;

public static class PayDateComputer
{
    public static DateOnly Compute(
        DateOnly periodEnd, string freqCode, string? convention, int offsetDays,
        IReadOnlySet<DateOnly>? holidays = null)
    {
        var raw = freqCode switch
        {
            "MONTHLY" => convention switch
            {
                "MONTH_END"       => new DateOnly(periodEnd.Year, periodEnd.Month,
                                         DateTime.DaysInMonth(periodEnd.Year, periodEnd.Month)),
                "MID_MONTH"       => new DateOnly(periodEnd.Year, periodEnd.Month, 15),
                "FIRST_FOLLOWING" => new DateOnly(periodEnd.Year, periodEnd.Month, 1).AddMonths(1),
                "FIXED_25"        => new DateOnly(periodEnd.Year, periodEnd.Month, 25),
                _                 => periodEnd.AddDays(offsetDays)
            },
            "SEMI_MONTHLY" => convention switch
            {
                // Pay on the period-close date — no lag.  Monthly salaried (current pay).
                "SM_15_AND_END" => periodEnd.Day <= 15
                    ? new DateOnly(periodEnd.Year, periodEnd.Month, 15)
                    : new DateOnly(periodEnd.Year, periodEnd.Month,
                          DateTime.DaysInMonth(periodEnd.Year, periodEnd.Month)),
                // Period A (1st–15th) pays on the 15th; Period B (16th–last) pays on 1st of next month.
                "SM_1_AND_15"  => periodEnd.Day <= 15
                    ? new DateOnly(periodEnd.Year, periodEnd.Month, 15)
                    : new DateOnly(periodEnd.Year, periodEnd.Month, 1).AddMonths(1),
                // Period A pays on 25th (10-day lag); Period B pays on 10th of next month (10-day lag).
                "SM_10_AND_25" => periodEnd.Day <= 15
                    ? new DateOnly(periodEnd.Year, periodEnd.Month, 25)
                    : new DateOnly(periodEnd.Year, periodEnd.Month, 10).AddMonths(1),
                _              => periodEnd.AddDays(offsetDays)
            },
            _ => periodEnd.AddDays(offsetDays)
        };

        return ShiftToBusinessDay(raw, holidays);
    }

    // Step backward one day at a time until a business day is reached.
    // Always pay early, never late.
    public static DateOnly ShiftToBusinessDay(DateOnly date, IReadOnlySet<DateOnly>? holidays = null)
    {
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
               (holidays is not null && holidays.Contains(date)))
        {
            date = date.AddDays(-1);
        }
        return date;
    }

    /// <summary>
    /// Subtracts <paramref name="bankingDays"/> business days from <paramref name="date"/>,
    /// skipping weekends and any supplied holidays.
    /// </summary>
    public static DateOnly SubtractBankingDays(DateOnly date, int bankingDays,
        IReadOnlySet<DateOnly>? holidays = null)
    {
        int remaining = bankingDays;
        while (remaining > 0)
        {
            date = date.AddDays(-1);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                (holidays is null || !holidays.Contains(date)))
                remaining--;
        }
        return date;
    }

    public static bool WillHaveExtraPeriod(DateOnly firstPeriodStart, int year, string freqCode)
    {
        if (freqCode is not ("WEEKLY" or "BIWEEKLY")) return false;
        int step     = freqCode == "WEEKLY" ? 7 : 14;
        int standard = freqCode == "WEEKLY" ? 52 : 26;
        int count    = 0;
        for (var s = firstPeriodStart; s.Year == year; s = s.AddDays(step))
            count++;
        return count > standard;
    }
}
