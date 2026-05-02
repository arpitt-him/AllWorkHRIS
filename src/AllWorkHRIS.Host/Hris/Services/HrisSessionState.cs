namespace AllWorkHRIS.Host.Hris.Services;

public interface IHrisSessionState
{
    Guid?   SelectedLegalEntityId   { get; }
    string? SelectedLegalEntityName { get; }
    bool    HasEntity               { get; }
    bool    IsLocked                { get; }
    void    SetEntity(Guid entityId, string entityName);
    void    Lock();
    void    Unlock();
    event Action? OnChanged;
}

public sealed class HrisSessionState : IHrisSessionState
{
    private Guid?   _entityId;
    private string? _entityName;
    private bool    _locked;

    public Guid?   SelectedLegalEntityId   => _entityId;
    public string? SelectedLegalEntityName => _entityName;
    public bool    HasEntity               => _entityId.HasValue;
    public bool    IsLocked                => _locked;

    public event Action? OnChanged;

    public void SetEntity(Guid entityId, string entityName)
    {
        _entityId   = entityId;
        _entityName = entityName;
        OnChanged?.Invoke();
    }

    public void Lock()
    {
        _locked = true;
        OnChanged?.Invoke();
    }

    public void Unlock()
    {
        _locked = false;
        OnChanged?.Invoke();
    }
}
