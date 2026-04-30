namespace AllWorkHRIS.Core.Temporal;

public interface ITemporalOverrideService
{
    bool     IsEnabled       { get; }
    bool     IsOverrideActive { get; }
    DateOnly? OverrideDate   { get; }
    void SetOverride(DateOnly date);
    void ClearOverride();
}

public sealed class NullTemporalOverrideService : ITemporalOverrideService
{
    public bool      IsEnabled        => false;
    public bool      IsOverrideActive => false;
    public DateOnly? OverrideDate     => null;
    public void SetOverride(DateOnly date) { }
    public void ClearOverride() { }
}
