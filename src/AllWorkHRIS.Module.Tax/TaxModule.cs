using System.Composition;
using Autofac;
using AllWorkHRIS.Core.Composition;
using AllWorkHRIS.Core.Pipeline;
using AllWorkHRIS.Module.Tax.Repositories;
using AllWorkHRIS.Module.Tax.Services;

namespace AllWorkHRIS.Module.Tax;

[Export(typeof(IPlatformModule))]
public sealed class TaxModule : IPlatformModule
{
    public string  ModuleName        => "Tax";
    public string  ModuleVersion     => "0.1.0";
    public string? ModuleDescription => "Payroll calculation pipeline — tax withholdings, social insurance, and employer contributions.";

    public void Register(ContainerBuilder builder)
    {
        builder.RegisterType<TaxRateRepository>()
               .As<ITaxRateRepository>()
               .InstancePerLifetimeScope();

        builder.RegisterType<TaxFormSubmissionRepository>()
               .As<ITaxFormSubmissionRepository>()
               .InstancePerLifetimeScope();

        builder.RegisterType<EmploymentJurisdictionLookup>()
               .As<IEmploymentJurisdictionLookup>()
               .InstancePerLifetimeScope();

        // Overrides NullPayrollPipelineService registered in Host (last-registration-wins)
        builder.RegisterType<PayrollPipelineService>()
               .As<IPayrollPipelineService>()
               .InstancePerLifetimeScope();
    }

    public IEnumerable<MenuContribution> GetMenuContributions()
    {
        yield return new MenuContribution
        {
            Label        = "Tax Configuration",
            Href         = null,
            Icon         = "TaxIcon",
            SortOrder    = 30,
            RequiredRole = "TaxAdmin,ComplianceReviewer,PayrollAdmin",
            BadgeLabel   = "TAX",
            AccentColor  = "var(--module-tax)"
        };
        yield return new MenuContribution
        {
            Label        = "Rate Reference",
            Href         = "/config/tax/reference",
            Icon         = "TaxIcon",
            SortOrder    = 1,
            RequiredRole = "TaxAdmin,ComplianceReviewer,PayrollAdmin,SystemAdmin",
            ParentLabel  = "Tax Configuration"
        };
        yield return new MenuContribution
        {
            Label        = "Calculation Steps",
            Href         = "/config/tax/steps",
            Icon         = "TaxIcon",
            SortOrder    = 2,
            RequiredRole = "TaxAdmin,ComplianceReviewer",
            ParentLabel  = "Tax Configuration"
        };
        yield return new MenuContribution
        {
            Label        = "Form Fields",
            Href         = "/config/tax/form-fields",
            Icon         = "TaxIcon",
            SortOrder    = 3,
            RequiredRole = "TaxAdmin,ComplianceReviewer",
            ParentLabel  = "Tax Configuration"
        };
        yield return new MenuContribution
        {
            Label        = "Review & Approve",
            Href         = "/config/tax/review",
            Icon         = "TaxIcon",
            SortOrder    = 4,
            RequiredRole = "ComplianceReviewer",
            ParentLabel  = "Tax Configuration"
        };
        yield return new MenuContribution
        {
            Label        = "Preview Sandbox",
            Href         = "/config/tax/preview",
            Icon         = "TaxIcon",
            SortOrder    = 5,
            RequiredRole = "TaxAdmin,ComplianceReviewer",
            ParentLabel  = "Tax Configuration"
        };
    }
}
