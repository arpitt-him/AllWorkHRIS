namespace AllWorkHRIS.Module.TimeAttendance.Domain;

public sealed class DomainException : Exception
{
    public string? ExceptionCode { get; init; }
    public DomainException(string message) : base(message) { }
}

public sealed class NotFoundException : Exception
{
    public NotFoundException(string typeName, object id)
        : base($"{typeName} '{id}' not found.") { }
}

public sealed class InvalidStateTransitionException : Exception
{
    public InvalidStateTransitionException(string from, string to)
        : base($"Invalid state transition from {from} to {to}.") { }
}

public sealed class AuthorizationException : Exception
{
    public AuthorizationException(string message) : base(message) { }
}
