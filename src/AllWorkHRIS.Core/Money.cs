namespace AllWorkHRIS.Core;

/// <summary>
/// Platform currency-rounding policy: round to the cent (2 dp) using round-half-up
/// (<see cref="MidpointRounding.AwayFromZero"/>) — the commercial convention payroll
/// and tax systems use.
///
/// Use <see cref="Round"/> wherever a money amount is <b>finalized</b> — earnings,
/// deductions, employer contributions, employer match, and taxes — so the figure that
/// hits net pay, a result line, and an accumulator is the exact transacted cent. Keep
/// higher precision only for intermediate values (rates, per-period derivations) that
/// are themselves rounded again at the final step.
/// </summary>
public static class Money
{
    /// <summary>Rounds a money amount to the cent, half-up (away from zero).</summary>
    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
