namespace AllWorkHRIS.Host.SharedUI;

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
