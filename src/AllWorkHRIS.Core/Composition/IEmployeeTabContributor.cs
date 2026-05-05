namespace AllWorkHRIS.Core.Composition;

public interface IEmployeeTabContributor
{
    string  Label        { get; }
    int     SortOrder    { get; }
    string? RequiredRole { get; }
    Type    ComponentType { get; }
}
