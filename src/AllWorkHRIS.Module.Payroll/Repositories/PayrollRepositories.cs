using System.Text.Json;
using Dapper;
using AllWorkHRIS.Core.Audit;
using AllWorkHRIS.Core.Data;
using AllWorkHRIS.Module.Payroll.Domain.Accumulators;
using AllWorkHRIS.Module.Payroll.Domain.Calendar;
using AllWorkHRIS.Module.Payroll.Domain.Profile;
using AllWorkHRIS.Module.Payroll.Domain.ResultSet;
using AllWorkHRIS.Module.Payroll.Domain.Results;
using AllWorkHRIS.Module.Payroll.Domain.Run;

namespace AllWorkHRIS.Module.Payroll.Repositories;

// ============================================================
// PAYROLL RUN
// ============================================================

public sealed class PayrollRunRepository : IPayrollRunRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PayrollRunRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<PayrollRun?> GetByIdAsync(Guid runId)
    {
        const string sql = "SELECT * FROM payroll_run WHERE run_id = @RunId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PayrollRun>(sql, new { RunId = runId });
    }

    public async Task<IReadOnlyList<PayrollRun>> GetByContextAsync(Guid payrollContextId)
    {
        const string sql = """
            SELECT * FROM payroll_run
            WHERE payroll_context_id = @PayrollContextId
            ORDER BY creation_timestamp DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollRun>(sql, new { PayrollContextId = payrollContextId })).ToList();
    }

    public async Task<bool> HasOpenRunForPeriodAsync(Guid payrollContextId, Guid periodId)
    {
        // An "open" run is any run not in a terminal state (CLOSED, CANCELLED, FAILED)
        const string sql = """
            SELECT COUNT(1) FROM payroll_run
            WHERE payroll_context_id = @PayrollContextId
              AND period_id           = @PeriodId
              AND run_status_id NOT IN (
                  SELECT id FROM lkp_run_status WHERE code IN ('CLOSED', 'CANCELLED', 'FAILED')
              )
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql,
            new { PayrollContextId = payrollContextId, PeriodId = periodId }) > 0;
    }

    public async Task<Guid> InsertAsync(PayrollRun run)
    {
        const string sql = """
            INSERT INTO payroll_run (
                run_id, payroll_context_id, period_id, pay_date,
                run_type_id, run_status_id, run_description,
                parent_run_id, related_run_group_id, rule_and_config_version_ref,
                temporal_override_active_flag, temporal_override_date,
                initiated_by, run_start_timestamp, run_end_timestamp,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @RunId, @PayrollContextId, @PeriodId, @PayDate,
                @RunTypeId, @RunStatusId, @RunDescription,
                @ParentRunId, @RelatedRunGroupId, @RuleAndConfigVersionRef,
                @TemporalOverrideActiveFlag, @TemporalOverrideDate,
                @InitiatedBy, @RunStartTimestamp, @RunEndTimestamp,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            run.RunId,
            run.PayrollContextId,
            run.PeriodId,
            PayDate                    = run.PayDate.ToDateTime(TimeOnly.MinValue),
            run.RunTypeId,
            run.RunStatusId,
            run.RunDescription,
            run.ParentRunId,
            run.RelatedRunGroupId,
            run.RuleAndConfigVersionRef,
            run.TemporalOverrideActiveFlag,
            TemporalOverrideDate       = run.TemporalOverrideDate?.ToDateTime(TimeOnly.MinValue),
            run.InitiatedBy,
            run.RunStartTimestamp,
            run.RunEndTimestamp,
            run.CreatedBy,
            run.CreationTimestamp,
            run.LastUpdatedBy,
            run.LastUpdateTimestamp
        });
        return run.RunId;
    }

    public async Task UpdateStatusAsync(Guid runId, int statusId, Guid updatedBy)
    {
        const string sql = """
            UPDATE payroll_run
            SET run_status_id       = @StatusId,
                last_updated_by     = @UpdatedBy,
                last_update_timestamp = CURRENT_TIMESTAMP
            WHERE run_id = @RunId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { RunId = runId, StatusId = statusId, UpdatedBy = updatedBy }, commandTimeout: 30);
    }

    public async Task SetRunTimestampsAsync(Guid runId, DateTimeOffset startTimestamp,
        DateTimeOffset? endTimestamp, Guid updatedBy)
    {
        const string sql = """
            UPDATE payroll_run
            SET run_start_timestamp   = @Start,
                run_end_timestamp     = @End,
                last_updated_by       = @UpdatedBy,
                last_update_timestamp = CURRENT_TIMESTAMP
            WHERE run_id = @RunId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            RunId     = runId,
            Start     = startTimestamp,
            End       = endTimestamp,
            UpdatedBy = updatedBy
        }, commandTimeout: 30);
    }

    public async Task InsertRunExceptionAsync(PayrollRunException exception)
    {
        const string sql = """
            INSERT INTO payroll_run_exception
                (run_exception_id, run_id, employment_id, exception_code, exception_message, created_timestamp)
            VALUES
                (@RunExceptionId, @RunId, @EmploymentId, @ExceptionCode, @ExceptionMessage, @CreatedTimestamp)
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            exception.RunExceptionId,
            exception.RunId,
            exception.EmploymentId,
            exception.ExceptionCode,
            exception.ExceptionMessage,
            exception.CreatedTimestamp
        });
    }

    public async Task<IReadOnlyList<PayrollRunException>> GetRunExceptionsAsync(Guid runId)
    {
        const string sql = """
            SELECT run_exception_id, run_id, employment_id, exception_code, exception_message, created_timestamp
            FROM payroll_run_exception
            WHERE run_id = @RunId
            ORDER BY created_timestamp
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollRunException>(sql, new { RunId = runId })).ToList();
    }
}

// ============================================================
// PAYROLL RUN RESULT SET
// ============================================================

public sealed class PayrollRunResultSetRepository : IPayrollRunResultSetRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PayrollRunResultSetRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<PayrollRunResultSet?> GetByIdAsync(Guid resultSetId)
    {
        const string sql = """
            SELECT * FROM payroll_run_result_set
            WHERE payroll_run_result_set_id = @ResultSetId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PayrollRunResultSet>(sql, new { ResultSetId = resultSetId });
    }

    public async Task<IReadOnlyList<PayrollRunResultSet>> GetByRunIdAsync(Guid runId)
    {
        const string sql = """
            SELECT * FROM payroll_run_result_set
            WHERE payroll_run_id = @RunId
            ORDER BY created_timestamp
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollRunResultSet>(sql, new { RunId = runId })).ToList();
    }

    public async Task<Guid> InsertAsync(PayrollRunResultSet resultSet)
    {
        const string sql = """
            INSERT INTO payroll_run_result_set (
                payroll_run_result_set_id, payroll_run_id, run_scope_id,
                source_period_id, execution_period_id,
                parent_payroll_run_result_set_id, root_payroll_run_result_set_id,
                result_set_lineage_sequence, correction_reference_id,
                result_set_status_id, result_set_type_id,
                execution_start_timestamp, execution_end_timestamp,
                approval_required_flag, approved_by_user_id, approval_timestamp,
                finalization_timestamp, created_timestamp, updated_timestamp
            ) VALUES (
                @PayrollRunResultSetId, @PayrollRunId, @RunScopeId,
                @SourcePeriodId, @ExecutionPeriodId,
                @ParentPayrollRunResultSetId, @RootPayrollRunResultSetId,
                @ResultSetLineageSequence, @CorrectionReferenceId,
                @ResultSetStatusId, @ResultSetTypeId,
                @ExecutionStartTimestamp, @ExecutionEndTimestamp,
                @ApprovalRequiredFlag, @ApprovedByUserId, @ApprovalTimestamp,
                @FinalizationTimestamp, @CreatedTimestamp, @UpdatedTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, resultSet);
        return resultSet.PayrollRunResultSetId;
    }

    public async Task UpdateStatusAsync(Guid resultSetId, int statusId)
    {
        const string sql = """
            UPDATE payroll_run_result_set
            SET result_set_status_id = @StatusId,
                updated_timestamp    = CURRENT_TIMESTAMP
            WHERE payroll_run_result_set_id = @ResultSetId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { ResultSetId = resultSetId, StatusId = statusId }, commandTimeout: 30);
    }

    public async Task SetTimestampsAsync(Guid resultSetId, DateTimeOffset? startTimestamp,
        DateTimeOffset? endTimestamp)
    {
        const string sql = """
            UPDATE payroll_run_result_set
            SET execution_start_timestamp = @Start,
                execution_end_timestamp   = @End,
                updated_timestamp         = CURRENT_TIMESTAMP
            WHERE payroll_run_result_set_id = @ResultSetId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { ResultSetId = resultSetId, Start = startTimestamp, End = endTimestamp });
    }
}

// ============================================================
// EMPLOYEE PAYROLL RESULT
// ============================================================

public sealed class EmployeePayrollResultRepository : IEmployeePayrollResultRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public EmployeePayrollResultRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<EmployeePayrollResult?> GetByIdAsync(Guid resultId)
    {
        const string sql = """
            SELECT * FROM employee_payroll_result
            WHERE employee_payroll_result_id = @ResultId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<EmployeePayrollResult>(sql, new { ResultId = resultId });
    }

    public async Task<IReadOnlyList<EmployeePayrollResult>> GetByResultSetIdAsync(Guid resultSetId)
    {
        const string sql = """
            SELECT * FROM employee_payroll_result
            WHERE payroll_run_result_set_id = @ResultSetId
            ORDER BY created_timestamp
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<EmployeePayrollResult>(sql, new { ResultSetId = resultSetId })).ToList();
    }

    public async Task<IReadOnlyList<EmployeePayrollResult>> GetByRunIdAsync(Guid runId)
    {
        const string sql = """
            SELECT * FROM employee_payroll_result
            WHERE payroll_run_id = @RunId
            ORDER BY created_timestamp
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<EmployeePayrollResult>(sql, new { RunId = runId })).ToList();
    }

    public async Task<Guid> InsertAsync(EmployeePayrollResult result)
    {
        const string sql = """
            INSERT INTO employee_payroll_result (
                employee_payroll_result_id, payroll_run_result_set_id, payroll_run_id, run_scope_id,
                employment_id, person_id, payroll_context_id,
                source_period_id, execution_period_id,
                parent_employee_payroll_result_id, root_employee_payroll_result_id,
                result_lineage_sequence, correction_reference_id, result_status_id,
                pay_period_start_date, pay_period_end_date, pay_date,
                gross_pay_amount, total_deductions_amount, total_employee_tax_amount,
                total_employer_contribution_amount, net_pay_amount,
                created_timestamp, updated_timestamp
            ) VALUES (
                @EmployeePayrollResultId, @PayrollRunResultSetId, @PayrollRunId, @RunScopeId,
                @EmploymentId, @PersonId, @PayrollContextId,
                @SourcePeriodId, @ExecutionPeriodId,
                @ParentEmployeePayrollResultId, @RootEmployeePayrollResultId,
                @ResultLineageSequence, @CorrectionReferenceId, @ResultStatusId,
                @PayPeriodStartDate, @PayPeriodEndDate, @PayDate,
                @GrossPayAmount, @TotalDeductionsAmount, @TotalEmployeeTaxAmount,
                @TotalEmployerContributionAmount, @NetPayAmount,
                @CreatedTimestamp, @UpdatedTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            result.EmployeePayrollResultId,
            result.PayrollRunResultSetId,
            result.PayrollRunId,
            result.RunScopeId,
            result.EmploymentId,
            result.PersonId,
            result.PayrollContextId,
            result.SourcePeriodId,
            result.ExecutionPeriodId,
            result.ParentEmployeePayrollResultId,
            result.RootEmployeePayrollResultId,
            result.ResultLineageSequence,
            result.CorrectionReferenceId,
            result.ResultStatusId,
            PayPeriodStartDate = result.PayPeriodStartDate.ToDateTime(TimeOnly.MinValue),
            PayPeriodEndDate   = result.PayPeriodEndDate.ToDateTime(TimeOnly.MinValue),
            PayDate            = result.PayDate.ToDateTime(TimeOnly.MinValue),
            result.GrossPayAmount,
            result.TotalDeductionsAmount,
            result.TotalEmployeeTaxAmount,
            result.TotalEmployerContributionAmount,
            result.NetPayAmount,
            result.CreatedTimestamp,
            result.UpdatedTimestamp
        });
        return result.EmployeePayrollResultId;
    }

    public async Task UpdateStatusAsync(Guid resultId, int statusId)
    {
        const string sql = """
            UPDATE employee_payroll_result
            SET result_status_id  = @StatusId,
                updated_timestamp = CURRENT_TIMESTAMP
            WHERE employee_payroll_result_id = @ResultId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { ResultId = resultId, StatusId = statusId });
    }

    public async Task UpdateTotalsAsync(Guid resultId, decimal grossPay, decimal totalDeductions,
        decimal totalEmployeeTax, decimal totalEmployerContribution, decimal netPay)
    {
        const string sql = """
            UPDATE employee_payroll_result
            SET gross_pay_amount                   = @GrossPay,
                total_deductions_amount            = @TotalDeductions,
                total_employee_tax_amount          = @TotalEmployeeTax,
                total_employer_contribution_amount = @TotalEmployerContribution,
                net_pay_amount                     = @NetPay,
                updated_timestamp                  = CURRENT_TIMESTAMP
            WHERE employee_payroll_result_id = @ResultId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            ResultId                  = resultId,
            GrossPay                  = grossPay,
            TotalDeductions           = totalDeductions,
            TotalEmployeeTax          = totalEmployeeTax,
            TotalEmployerContribution = totalEmployerContribution,
            NetPay                    = netPay
        });
    }
}

// ============================================================
// RESULT LINES
// ============================================================

public sealed class ResultLineRepository : IResultLineRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public ResultLineRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task InsertEarningsLineAsync(EarningsResultLine line)
    {
        const string sql = """
            INSERT INTO earnings_result_line (
                earnings_result_line_id, employee_payroll_result_id, employment_id,
                earnings_code, earnings_description, quantity, rate, calculated_amount,
                jurisdiction_split_flag, taxable_flag, accumulator_impact_flag,
                source_rule_version_id, correction_flag, corrects_line_id, creation_timestamp
            ) VALUES (
                @EarningsResultLineId, @EmployeePayrollResultId, @EmploymentId,
                @EarningsCode, @EarningsDescription, @Quantity, @Rate, @CalculatedAmount,
                @JurisdictionSplitFlag, @TaxableFlag, @AccumulatorImpactFlag,
                @SourceRuleVersionId, @CorrectionFlag, @CorrectsLineId, @CreationTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, line);
    }

    public async Task InsertDeductionLineAsync(DeductionResultLine line)
    {
        const string sql = """
            INSERT INTO deduction_result_line (
                deduction_result_line_id, employee_payroll_result_id, employment_id,
                deduction_code, deduction_description, calculated_amount,
                pre_tax_flag, cash_impact_flag, accumulator_impact_flag,
                source_rule_version_id, correction_flag, corrects_line_id, creation_timestamp
            ) VALUES (
                @DeductionResultLineId, @EmployeePayrollResultId, @EmploymentId,
                @DeductionCode, @DeductionDescription, @CalculatedAmount,
                @PreTaxFlag, @CashImpactFlag, @AccumulatorImpactFlag,
                @SourceRuleVersionId, @CorrectionFlag, @CorrectsLineId, @CreationTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, line);
    }

    public async Task InsertTaxLineAsync(TaxResultLine line)
    {
        const string sql = """
            INSERT INTO tax_result_line (
                tax_result_line_id, employee_payroll_result_id, employment_id,
                jurisdiction_id, tax_code, tax_description,
                taxable_wages_amount, calculated_amount, employer_flag,
                accumulator_impact_flag, source_rule_version_id,
                correction_flag, corrects_line_id, creation_timestamp
            ) VALUES (
                @TaxResultLineId, @EmployeePayrollResultId, @EmploymentId,
                @JurisdictionId, @TaxCode, @TaxDescription,
                @TaxableWagesAmount, @CalculatedAmount, @EmployerFlag,
                @AccumulatorImpactFlag, @SourceRuleVersionId,
                @CorrectionFlag, @CorrectsLineId, @CreationTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, line);
    }

    public async Task InsertEmployerContributionLineAsync(EmployerContributionResultLine line)
    {
        const string sql = """
            INSERT INTO employer_contribution_result_line (
                employer_contribution_result_line_id, employee_payroll_result_id, employment_id,
                contribution_code, contribution_description, calculated_amount,
                accumulator_impact_flag, source_rule_version_id,
                correction_flag, corrects_line_id, creation_timestamp
            ) VALUES (
                @EmployerContributionResultLineId, @EmployeePayrollResultId, @EmploymentId,
                @ContributionCode, @ContributionDescription, @CalculatedAmount,
                @AccumulatorImpactFlag, @SourceRuleVersionId,
                @CorrectionFlag, @CorrectsLineId, @CreationTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, line);
    }

    public async Task<IReadOnlyList<EarningsResultLine>> GetEarningsByResultIdAsync(Guid employeePayrollResultId)
    {
        const string sql = """
            SELECT * FROM earnings_result_line
            WHERE employee_payroll_result_id = @ResultId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<EarningsResultLine>(sql, new { ResultId = employeePayrollResultId })).ToList();
    }

    public async Task<IReadOnlyList<DeductionResultLine>> GetDeductionsByResultIdAsync(Guid employeePayrollResultId)
    {
        const string sql = """
            SELECT * FROM deduction_result_line
            WHERE employee_payroll_result_id = @ResultId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<DeductionResultLine>(sql, new { ResultId = employeePayrollResultId })).ToList();
    }

    public async Task<IReadOnlyList<TaxResultLine>> GetTaxLinesByResultIdAsync(Guid employeePayrollResultId)
    {
        const string sql = """
            SELECT * FROM tax_result_line
            WHERE employee_payroll_result_id = @ResultId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<TaxResultLine>(sql, new { ResultId = employeePayrollResultId })).ToList();
    }

    public async Task<IReadOnlyList<EmployerContributionResultLine>> GetContributionsByResultIdAsync(
        Guid employeePayrollResultId)
    {
        const string sql = """
            SELECT * FROM employer_contribution_result_line
            WHERE employee_payroll_result_id = @ResultId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<EmployerContributionResultLine>(sql,
            new { ResultId = employeePayrollResultId })).ToList();
    }
}

// ============================================================
// ACCUMULATORS
// ============================================================

public sealed class AccumulatorRepository : IAccumulatorRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public AccumulatorRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<AccumulatorDefinition?> GetDefinitionByCodeAsync(string accumulatorCode, DateOnly asOf)
    {
        const string sql = """
            SELECT * FROM accumulator_definition
            WHERE accumulator_code = @AccumulatorCode
              AND (effective_end_date IS NULL OR effective_end_date >= @AsOf)
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<AccumulatorDefinition>(sql,
            new { AccumulatorCode = accumulatorCode, AsOf = asOf.ToDateTime(TimeOnly.MinValue) });
    }

    public async Task<IReadOnlyList<AccumulatorDefinition>> GetAllActiveDefinitionsAsync(DateOnly asOf)
    {
        const string sql = """
            SELECT * FROM accumulator_definition
            WHERE effective_end_date IS NULL OR effective_end_date >= @AsOf
            ORDER BY accumulator_code
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<AccumulatorDefinition>(sql,
            new { AsOf = asOf.ToDateTime(TimeOnly.MinValue) })).ToList();
    }

    public async Task<AccumulatorBalance?> GetBalanceAsync(Guid accumulatorDefinitionId, Guid? employmentId,
        Guid? legalEntityId, Guid periodId)
    {
        // periodId maps to calendar_context_id in the balance table
        if (employmentId.HasValue)
        {
            const string sql = """
                SELECT * FROM accumulator_balance
                WHERE accumulator_definition_id = @AccumulatorDefinitionId
                  AND participant_id            = @EmploymentId
                  AND calendar_context_id       = @PeriodId
                """;
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<AccumulatorBalance>(sql,
                new { AccumulatorDefinitionId = accumulatorDefinitionId,
                      EmploymentId = employmentId,
                      PeriodId = periodId });
        }
        else
        {
            const string sql = """
                SELECT * FROM accumulator_balance
                WHERE accumulator_definition_id = @AccumulatorDefinitionId
                  AND employer_id               = @LegalEntityId
                  AND calendar_context_id       = @PeriodId
                """;
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<AccumulatorBalance>(sql,
                new { AccumulatorDefinitionId = accumulatorDefinitionId,
                      LegalEntityId = legalEntityId,
                      PeriodId = periodId });
        }
    }

    public async Task UpsertBalanceAsync(AccumulatorBalance balance)
    {
        const string sql = """
            INSERT INTO accumulator_balance (
                accumulator_id, accumulator_definition_id, accumulator_family_id,
                scope_type_id, participant_id, employer_id, jurisdiction_id, plan_id,
                period_context_id, calendar_context_id, current_value,
                balance_status_id, last_updated_run_id, last_updated_result_set_id,
                last_update_timestamp
            ) VALUES (
                @AccumulatorId, @AccumulatorDefinitionId, @AccumulatorFamilyId,
                @ScopeTypeId, @ParticipantId, @EmployerId, @JurisdictionId, @PlanId,
                @PeriodContextId, @CalendarContextId, @CurrentValue,
                @BalanceStatusId, @LastUpdatedRunId, @LastUpdatedResultSetId,
                @LastUpdateTimestamp
            )
            ON CONFLICT (accumulator_id) DO UPDATE SET
                current_value              = EXCLUDED.current_value,
                balance_status_id          = EXCLUDED.balance_status_id,
                last_updated_run_id        = EXCLUDED.last_updated_run_id,
                last_updated_result_set_id = EXCLUDED.last_updated_result_set_id,
                last_update_timestamp      = EXCLUDED.last_update_timestamp
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, balance);
    }

    public async Task InsertImpactAsync(AccumulatorImpact impact)
    {
        const string sql = """
            INSERT INTO accumulator_impact (
                accumulator_impact_id, accumulator_definition_id,
                payroll_run_result_set_id, employee_payroll_result_id,
                payroll_run_id, employment_id, person_id,
                impact_status_id, impact_source_type_id, source_object_id,
                prior_value, delta_value, new_value,
                posting_direction_id, scope_type_id, scope_object_id,
                jurisdiction_id, rule_pack_id, rule_version_id,
                retroactive_flag, reversal_flag, correction_flag,
                prior_accumulator_impact_id, notes,
                impact_timestamp, created_timestamp, updated_timestamp
            ) VALUES (
                @AccumulatorImpactId, @AccumulatorDefinitionId,
                @PayrollRunResultSetId, @EmployeePayrollResultId,
                @PayrollRunId, @EmploymentId, @PersonId,
                @ImpactStatusId, @ImpactSourceTypeId, @SourceObjectId,
                @PriorValue, @DeltaValue, @NewValue,
                @PostingDirectionId, @ScopeTypeId, @ScopeObjectId,
                @JurisdictionId, @RulePackId, @RuleVersionId,
                @RetroactiveFlag, @ReversalFlag, @CorrectionFlag,
                @PriorAccumulatorImpactId, @Notes,
                @ImpactTimestamp, @CreatedTimestamp, @UpdatedTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, impact);
    }

    public async Task InsertContributionAsync(AccumulatorContribution contribution)
    {
        const string sql = """
            INSERT INTO accumulator_contribution (
                contribution_id, accumulator_id, accumulator_impact_id,
                accumulator_definition_id, parent_contribution_id, root_contribution_id,
                contribution_lineage_sequence, correction_reference_id,
                source_run_id, source_result_set_id, source_employee_result_id,
                source_period_id, execution_period_id, employment_id,
                scope_type_id, scope_object_id,
                contribution_amount, contribution_type_id,
                before_value, after_value, creation_timestamp
            ) VALUES (
                @ContributionId, @AccumulatorId, @AccumulatorImpactId,
                @AccumulatorDefinitionId, @ParentContributionId, @RootContributionId,
                @ContributionLineageSequence, @CorrectionReferenceId,
                @SourceRunId, @SourceResultSetId, @SourceEmployeeResultId,
                @SourcePeriodId, @ExecutionPeriodId, @EmploymentId,
                @ScopeTypeId, @ScopeObjectId,
                @ContributionAmount, @ContributionTypeId,
                @BeforeValue, @AfterValue, @CreationTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, contribution);
    }

    public async Task<IReadOnlyList<AccumulatorImpact>> GetImpactsByRunIdAsync(Guid runId)
    {
        const string sql = """
            SELECT * FROM accumulator_impact
            WHERE payroll_run_id = @RunId
            ORDER BY impact_timestamp
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<AccumulatorImpact>(sql, new { RunId = runId })).ToList();
    }

    public async Task<IReadOnlyList<AccumulatorImpact>> GetImpactsByEmploymentIdAsync(Guid employmentId)
    {
        const string sql = """
            SELECT * FROM accumulator_impact
            WHERE employment_id = @EmploymentId
            ORDER BY impact_timestamp DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<AccumulatorImpact>(sql, new { EmploymentId = employmentId })).ToList();
    }

    public async Task<IReadOnlyList<AccumulatorImpact>> GetImpactsByResultIdAsync(Guid employeePayrollResultId)
    {
        const string sql = """
            SELECT * FROM accumulator_impact
            WHERE employee_payroll_result_id = @ResultId
            ORDER BY impact_timestamp ASC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<AccumulatorImpact>(sql, new { ResultId = employeePayrollResultId })).ToList();
    }

    public async Task<IReadOnlyList<AccumulatorContribution>> GetContributionsByResultIdAsync(Guid employeePayrollResultId)
    {
        const string sql = """
            SELECT * FROM accumulator_contribution
            WHERE source_employee_result_id = @ResultId
            ORDER BY creation_timestamp ASC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<AccumulatorContribution>(sql, new { ResultId = employeePayrollResultId })).ToList();
    }

    public async Task RevertBalanceAsync(Guid accumulatorDefinitionId, Guid employmentId,
        Guid periodId, decimal targetValue, Guid runId, DateTimeOffset now)
    {
        const string sql = """
            UPDATE accumulator_balance
            SET    current_value         = @TargetValue,
                   last_updated_run_id   = @RunId,
                   last_update_timestamp = @Now
            WHERE  accumulator_definition_id = @AccumulatorDefinitionId
              AND  participant_id            = @EmploymentId
              AND  calendar_context_id       = @PeriodId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            AccumulatorDefinitionId = accumulatorDefinitionId,
            EmploymentId            = employmentId,
            PeriodId                = periodId,
            TargetValue             = targetValue,
            RunId                   = runId,
            Now                     = now
        });
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetYtdBalancesAsync(Guid employmentId, DateOnly asOf)
    {
        const string sql = """
            SELECT ad.accumulator_code, SUM(ab.current_value) AS period_sum
            FROM   accumulator_balance ab
            JOIN   accumulator_definition ad ON ad.accumulator_definition_id = ab.accumulator_definition_id
            JOIN   payroll_period pp          ON pp.period_id = ab.calendar_context_id
            WHERE  ab.participant_id    = @EmploymentId
              AND  ad.reset_type        = 'CALENDAR_YEAR'
              AND  pp.period_start_date >= @YearStart
              AND  pp.period_start_date <  @AsOf
            GROUP BY ad.accumulator_code
            """;

        using var conn = _connectionFactory.CreateConnection();
        var rows = await conn.QueryAsync(sql, new
        {
            EmploymentId = employmentId,
            YearStart    = new DateOnly(asOf.Year, 1, 1).ToDateTime(TimeOnly.MinValue),
            AsOf         = asOf.ToDateTime(TimeOnly.MinValue)
        });

        var result = new Dictionary<string, decimal>();
        foreach (var row in rows)
            result[(string)row.accumulator_code] = (decimal)row.period_sum;
        return result;
    }

    public async Task ApplyImpactChainAsync(
        AccumulatorImpact impact, AccumulatorContribution contribution,
        AccumulatorBalance balance, IUnitOfWork uow)
    {
        const string impactSql = """
            INSERT INTO accumulator_impact (
                accumulator_impact_id, accumulator_definition_id,
                payroll_run_result_set_id, employee_payroll_result_id,
                payroll_run_id, employment_id, person_id,
                impact_status_id, impact_source_type_id, source_object_id,
                prior_value, delta_value, new_value,
                posting_direction_id, scope_type_id, scope_object_id,
                jurisdiction_id, rule_pack_id, rule_version_id,
                retroactive_flag, reversal_flag, correction_flag,
                prior_accumulator_impact_id, notes,
                impact_timestamp, created_timestamp, updated_timestamp
            ) VALUES (
                @AccumulatorImpactId, @AccumulatorDefinitionId,
                @PayrollRunResultSetId, @EmployeePayrollResultId,
                @PayrollRunId, @EmploymentId, @PersonId,
                @ImpactStatusId, @ImpactSourceTypeId, @SourceObjectId,
                @PriorValue, @DeltaValue, @NewValue,
                @PostingDirectionId, @ScopeTypeId, @ScopeObjectId,
                @JurisdictionId, @RulePackId, @RuleVersionId,
                @RetroactiveFlag, @ReversalFlag, @CorrectionFlag,
                @PriorAccumulatorImpactId, @Notes,
                @ImpactTimestamp, @CreatedTimestamp, @UpdatedTimestamp
            )
            """;

        const string contributionSql = """
            INSERT INTO accumulator_contribution (
                contribution_id, accumulator_id, accumulator_impact_id,
                accumulator_definition_id, parent_contribution_id, root_contribution_id,
                contribution_lineage_sequence, correction_reference_id,
                source_run_id, source_result_set_id, source_employee_result_id,
                source_period_id, execution_period_id, employment_id,
                scope_type_id, scope_object_id,
                contribution_amount, contribution_type_id,
                before_value, after_value, creation_timestamp
            ) VALUES (
                @ContributionId, @AccumulatorId, @AccumulatorImpactId,
                @AccumulatorDefinitionId, @ParentContributionId, @RootContributionId,
                @ContributionLineageSequence, @CorrectionReferenceId,
                @SourceRunId, @SourceResultSetId, @SourceEmployeeResultId,
                @SourcePeriodId, @ExecutionPeriodId, @EmploymentId,
                @ScopeTypeId, @ScopeObjectId,
                @ContributionAmount, @ContributionTypeId,
                @BeforeValue, @AfterValue, @CreationTimestamp
            )
            """;

        const string balanceSql = """
            INSERT INTO accumulator_balance (
                accumulator_id, accumulator_definition_id, accumulator_family_id,
                scope_type_id, participant_id, employer_id, jurisdiction_id, plan_id,
                period_context_id, calendar_context_id, current_value,
                balance_status_id, last_updated_run_id, last_updated_result_set_id,
                last_update_timestamp
            ) VALUES (
                @AccumulatorId, @AccumulatorDefinitionId, @AccumulatorFamilyId,
                @ScopeTypeId, @ParticipantId, @EmployerId, @JurisdictionId, @PlanId,
                @PeriodContextId, @CalendarContextId, @CurrentValue,
                @BalanceStatusId, @LastUpdatedRunId, @LastUpdatedResultSetId,
                @LastUpdateTimestamp
            )
            ON CONFLICT (accumulator_id) DO UPDATE SET
                current_value              = EXCLUDED.current_value,
                balance_status_id          = EXCLUDED.balance_status_id,
                last_updated_run_id        = EXCLUDED.last_updated_run_id,
                last_updated_result_set_id = EXCLUDED.last_updated_result_set_id,
                last_update_timestamp      = EXCLUDED.last_update_timestamp
            """;

        await uow.Connection.ExecuteAsync(impactSql,       impact,       uow.Transaction);
        await uow.Connection.ExecuteAsync(contributionSql, contribution, uow.Transaction);
        await uow.Connection.ExecuteAsync(balanceSql,      balance,      uow.Transaction);
    }
}

// ============================================================
// PAYROLL CONTEXT
// ============================================================

public sealed class PayrollContextRepository : IPayrollContextRepository
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IAuditService      _auditService;

    public PayrollContextRepository(IConnectionFactory connectionFactory, IAuditService auditService)
    {
        _connectionFactory = connectionFactory;
        _auditService      = auditService;
    }

    public async Task<PayrollContext?> GetByIdAsync(Guid payrollContextId)
    {
        const string sql = "SELECT * FROM payroll_context WHERE payroll_context_id = @PayrollContextId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PayrollContext>(sql, new { PayrollContextId = payrollContextId });
    }

    public async Task<IReadOnlyList<PayrollContext>> GetAllAsync()
    {
        const string sql = "SELECT * FROM payroll_context ORDER BY payroll_context_code";
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollContext>(sql)).ToList();
    }

    public async Task<IReadOnlyList<PayrollContext>> GetAllActiveAsync()
    {
        const string sql = """
            SELECT * FROM payroll_context
            WHERE context_status = 'ACTIVE'
            ORDER BY payroll_context_code
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollContext>(sql)).ToList();
    }

    public async Task<IReadOnlyList<PayrollContext>> GetByLegalEntityAsync(Guid legalEntityId)
    {
        const string sql = """
            SELECT * FROM payroll_context
            WHERE legal_entity_id = @LegalEntityId
            ORDER BY payroll_context_code
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollContext>(sql, new { LegalEntityId = legalEntityId })).ToList();
    }

    public async Task<Guid> InsertContextAsync(PayrollContext context)
    {
        const string sql = """
            INSERT INTO payroll_context (
                payroll_context_id, payroll_context_code, payroll_context_name,
                legal_entity_id, pay_frequency_id, compensation_rate_type_id, context_status,
                parent_payroll_context_id, root_payroll_context_id,
                context_version_number, context_change_reason_code,
                effective_start_date, effective_end_date,
                pay_date_convention, pay_date_offset_days, cutoff_offset_days, extra_period_policy,
                ot_weekly_threshold_hours, workweek_start_day,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @PayrollContextId, @PayrollContextCode, @PayrollContextName,
                @LegalEntityId, @PayFrequencyId, @CompensationRateTypeId, @ContextStatus,
                @ParentPayrollContextId, @RootPayrollContextId,
                @ContextVersionNumber, @ContextChangeReasonCode,
                @EffectiveStartDate, @EffectiveEndDate,
                @PayDateConvention, @PayDateOffsetDays, @CutoffOffsetDays, @ExtraPeriodPolicy,
                @OtWeeklyThresholdHours, @WorkweekStartDay,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            context.PayrollContextId,
            context.PayrollContextCode,
            context.PayrollContextName,
            context.LegalEntityId,
            context.PayFrequencyId,
            context.CompensationRateTypeId,
            context.ContextStatus,
            context.ParentPayrollContextId,
            context.RootPayrollContextId,
            context.ContextVersionNumber,
            context.ContextChangeReasonCode,
            EffectiveStartDate = context.EffectiveStartDate.ToDateTime(TimeOnly.MinValue),
            EffectiveEndDate   = context.EffectiveEndDate?.ToDateTime(TimeOnly.MinValue),
            context.PayDateConvention,
            context.PayDateOffsetDays,
            context.CutoffOffsetDays,
            context.ExtraPeriodPolicy,
            context.OtWeeklyThresholdHours,
            context.WorkweekStartDay,
            context.CreatedBy,
            context.CreationTimestamp,
            context.LastUpdatedBy,
            context.LastUpdateTimestamp
        });

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:     "CREATE",
            EntityType:    "PayrollContext",
            EntityId:      context.PayrollContextId,
            ModuleName:    "PAYROLL",
            ChangeSummary: $"Created payroll context {context.PayrollContextCode}",
            AfterJson:     JsonSerializer.Serialize(new
            {
                context.PayrollContextCode,
                context.PayrollContextName,
                context.ContextStatus
            })
        ));

        return context.PayrollContextId;
    }

    public async Task<string?> DeleteContextAsync(Guid payrollContextId)
    {
        using var conn = _connectionFactory.CreateConnection();

        // Block if any run has ever been initiated against this context.
        // This is the sole guard: a run record means the context was used for live payroll
        // and must be preserved. Period status is not a reliable signal — mid-year contexts
        // auto-close all historical periods at generation time before any payroll runs.
        const string checkRuns = """
            SELECT COUNT(1) FROM payroll_run
            WHERE payroll_context_id = @Id
            """;
        if (await conn.ExecuteScalarAsync<int>(checkRuns, new { Id = payrollContextId }) > 0)
            return "This context has associated payroll runs and cannot be deleted.";

        // Safe to delete — remove enrollments, open periods, then the context record
        await conn.ExecuteAsync(
            "DELETE FROM payroll_profile WHERE payroll_context_id = @Id",
            new { Id = payrollContextId });

        await conn.ExecuteAsync(
            "DELETE FROM payroll_period WHERE payroll_context_id = @Id",
            new { Id = payrollContextId });

        await conn.ExecuteAsync(
            "DELETE FROM payroll_context WHERE payroll_context_id = @Id",
            new { Id = payrollContextId });

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:     "DELETE",
            EntityType:    "PayrollContext",
            EntityId:      payrollContextId,
            ModuleName:    "PAYROLL",
            ChangeSummary: $"Deleted payroll context {payrollContextId}"
        ));

        return null;
    }

    public async Task UpdateContextStatusAsync(Guid payrollContextId, string status, Guid updatedBy)
    {
        const string sql = """
            UPDATE payroll_context
            SET context_status        = @Status,
                last_updated_by       = @UpdatedBy,
                last_update_timestamp = CURRENT_TIMESTAMP
            WHERE payroll_context_id = @PayrollContextId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { PayrollContextId = payrollContextId, Status = status, UpdatedBy = updatedBy });

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:     "STATUS_CHANGE",
            EntityType:    "PayrollContext",
            EntityId:      payrollContextId,
            ModuleName:    "PAYROLL",
            ChangeSummary: $"Payroll context status changed to {status}",
            AfterJson:     JsonSerializer.Serialize(new { context_status = status })
        ));
    }

    public async Task<PayrollPeriod?> GetPeriodByIdAsync(Guid periodId)
    {
        const string sql = "SELECT * FROM payroll_period WHERE period_id = @PeriodId";
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PayrollPeriod>(sql, new { PeriodId = periodId });
    }

    public async Task<PayrollPeriod?> GetCurrentOpenPeriodAsync(Guid payrollContextId)
    {
        const string sql = """
            SELECT * FROM payroll_period
            WHERE payroll_context_id = @PayrollContextId
              AND calendar_status    = 'OPEN'
            ORDER BY period_year DESC, period_number DESC
            LIMIT 1
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PayrollPeriod>(sql, new { PayrollContextId = payrollContextId });
    }

    public async Task<IReadOnlyList<PayrollPeriod>> GetOpenPeriodsAsync(Guid payrollContextId)
    {
        const string sql = """
            SELECT * FROM payroll_period
            WHERE payroll_context_id = @PayrollContextId
              AND calendar_status    = 'OPEN'
            ORDER BY period_year, period_number
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollPeriod>(sql, new { PayrollContextId = payrollContextId })).ToList();
    }

    public async Task<IReadOnlyList<PayrollPeriod>> GetPeriodsByContextAsync(Guid payrollContextId, int year)
    {
        const string sql = """
            SELECT * FROM payroll_period
            WHERE payroll_context_id = @PayrollContextId
              AND period_year        = @Year
            ORDER BY period_number
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollPeriod>(sql,
            new { PayrollContextId = payrollContextId, Year = year })).ToList();
    }

    public async Task<Guid> InsertPeriodAsync(PayrollPeriod period)
    {
        const string sql = """
            INSERT INTO payroll_period (
                period_id, payroll_context_id, period_year, period_number,
                period_start_date, period_end_date, pay_date, input_cutoff_date,
                calculation_date, correction_window_close_date, finalization_date, transmission_date,
                calendar_status, parent_calendar_entry_id, root_calendar_entry_id,
                calendar_version_number, calendar_change_reason_code,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @PeriodId, @PayrollContextId, @PeriodYear, @PeriodNumber,
                @PeriodStartDate, @PeriodEndDate, @PayDate, @InputCutoffDate,
                @CalculationDate, @CorrectionWindowCloseDate, @FinalizationDate, @TransmissionDate,
                @CalendarStatus, @ParentCalendarEntryId, @RootCalendarEntryId,
                @CalendarVersionNumber, @CalendarChangeReasonCode,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            period.PeriodId,
            period.PayrollContextId,
            period.PeriodYear,
            period.PeriodNumber,
            PeriodStartDate            = period.PeriodStartDate.ToDateTime(TimeOnly.MinValue),
            PeriodEndDate              = period.PeriodEndDate.ToDateTime(TimeOnly.MinValue),
            PayDate                    = period.PayDate.ToDateTime(TimeOnly.MinValue),
            InputCutoffDate            = period.InputCutoffDate.ToDateTime(TimeOnly.MinValue),
            CalculationDate            = period.CalculationDate?.ToDateTime(TimeOnly.MinValue),
            CorrectionWindowCloseDate  = period.CorrectionWindowCloseDate?.ToDateTime(TimeOnly.MinValue),
            FinalizationDate           = period.FinalizationDate?.ToDateTime(TimeOnly.MinValue),
            TransmissionDate           = period.TransmissionDate?.ToDateTime(TimeOnly.MinValue),
            period.CalendarStatus,
            period.ParentCalendarEntryId,
            period.RootCalendarEntryId,
            period.CalendarVersionNumber,
            period.CalendarChangeReasonCode,
            period.CreatedBy,
            period.CreationTimestamp,
            period.LastUpdatedBy,
            period.LastUpdateTimestamp
        });
        return period.PeriodId;
    }

    public async Task<(int Deleted, int Skipped)> DeletePeriodsForYearAsync(Guid contextId, int year)
    {
        using var conn = _connectionFactory.CreateConnection();

        // Count periods that ARE referenced by a run — cannot be deleted
        const string countSkipped = """
            SELECT COUNT(*) FROM payroll_period pp
            WHERE pp.payroll_context_id = @ContextId
              AND pp.period_year        = @Year
              AND EXISTS (
                  SELECT 1 FROM payroll_run pr
                  WHERE pr.period_id = pp.period_id
              )
            """;
        int skipped = await conn.ExecuteScalarAsync<int>(countSkipped, new { ContextId = contextId, Year = year });

        // Delete only periods with no run references
        const string deleteSql = """
            DELETE FROM payroll_period
            WHERE payroll_context_id = @ContextId
              AND period_year        = @Year
              AND period_id NOT IN (
                  SELECT period_id FROM payroll_run
                  WHERE payroll_context_id = @ContextId
              )
            """;
        int deleted = await conn.ExecuteAsync(deleteSql, new { ContextId = contextId, Year = year });

        return (deleted, skipped);
    }

    public async Task UpdatePeriodStatusAsync(Guid periodId, string status, Guid updatedBy)
    {
        const string sql = """
            UPDATE payroll_period
            SET calendar_status       = @Status,
                last_updated_by       = @UpdatedBy,
                last_update_timestamp = CURRENT_TIMESTAMP
            WHERE period_id = @PeriodId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { PeriodId = periodId, Status = status, UpdatedBy = updatedBy });
    }

    public async Task UpdatePeriodRunDateAsync(Guid periodId, DateOnly? runDate, Guid updatedBy)
    {
        const string sql = """
            UPDATE payroll_period
            SET calculation_date      = @RunDate,
                last_updated_by       = @UpdatedBy,
                last_update_timestamp = CURRENT_TIMESTAMP
            WHERE period_id = @PeriodId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            PeriodId  = periodId,
            RunDate   = runDate.HasValue ? runDate.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null,
            UpdatedBy = updatedBy
        });

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:       "UPDATE",
            EntityType:      "PayrollPeriod",
            EntityId:        periodId,
            ModuleName:      "PAYROLL",
            ChangeSummary:   $"Period run date updated to {runDate?.ToString("yyyy-MM-dd") ?? "null"}",
            AfterJson:       JsonSerializer.Serialize(new { calculation_date = runDate?.ToString("yyyy-MM-dd") })
        ));
    }

    public async Task<int> GetPeriodsPerYearAsync(Guid payrollContextId)
    {
        const string sql = """
            SELECT f.periods_per_year
            FROM payroll_context pc
            JOIN lkp_pay_frequency f ON f.id = pc.pay_frequency_id
            WHERE pc.payroll_context_id = @PayrollContextId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, new { PayrollContextId = payrollContextId });
    }

    public async Task<(decimal? OtWeeklyThresholdHours, int? WorkweekStartDay)> GetLegalEntityDefaultsAsync(Guid legalEntityId)
    {
        const string sql = """
            SELECT ot_weekly_threshold_hours, default_workweek_start_day
            FROM org_unit
            WHERE org_unit_id = @LegalEntityId
            """;
        using var conn = _connectionFactory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync(sql, new { LegalEntityId = legalEntityId });
        if (row is null) return (null, null);
        return ((decimal?)row.ot_weekly_threshold_hours, (int?)row.default_workweek_start_day);
    }
}

// ============================================================
// PAYROLL COMPENSATION SNAPSHOT
// ============================================================

public sealed class PayrollCompensationSnapshotRepository : IPayrollCompensationSnapshotRepository
{
    private readonly IConnectionFactory _connectionFactory;

    public PayrollCompensationSnapshotRepository(IConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<CompensationSnapshot?> GetSnapshotAsync(Guid employmentId, DateOnly asOf)
    {
        const string sql = """
            SELECT
                cr.annual_equivalent,
                cr.base_rate,
                rt.code AS rate_type_code,
                fs.code AS flsa_status_code
            FROM  compensation_record       cr
            JOIN  lkp_compensation_rate_type rt  ON rt.id  = cr.rate_type_id
            JOIN  employment                 emp ON emp.employment_id = cr.employment_id
            JOIN  lkp_flsa_status            fs  ON fs.id  = emp.flsa_status_id
            WHERE cr.employment_id = @EmploymentId
              AND cr.primary_rate_flag = true
              AND cr.compensation_status_id = (
                  SELECT id FROM lkp_compensation_status WHERE code = 'ACTIVE'
              )
              AND cr.effective_start_date <= @AsOf
              AND (cr.effective_end_date IS NULL OR cr.effective_end_date >= @AsOf)
            ORDER BY cr.effective_start_date DESC
            FETCH FIRST 1 ROWS ONLY
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CompensationSnapshot>(sql, new
        {
            EmploymentId = employmentId,
            AsOf         = asOf.ToDateTime(TimeOnly.MinValue)
        });
    }
}

// ============================================================
// PAYROLL PROFILE
// ============================================================

public sealed class PayrollProfileRepository : IPayrollProfileRepository
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly IAuditService      _auditService;

    public PayrollProfileRepository(IConnectionFactory connectionFactory, IAuditService auditService)
    {
        _connectionFactory = connectionFactory;
        _auditService      = auditService;
    }

    public async Task<Guid> InsertAsync(PayrollProfile profile)
    {
        const string sql = """
            INSERT INTO payroll_profile (
                payroll_profile_id, employment_id, person_id, payroll_context_id,
                enrollment_status, effective_start_date, effective_end_date,
                final_pay_flag, blocking_tasks_cleared, enrollment_source,
                created_by, creation_timestamp, last_updated_by, last_update_timestamp
            ) VALUES (
                @PayrollProfileId, @EmploymentId, @PersonId, @PayrollContextId,
                @EnrollmentStatus, @EffectiveStartDate, @EffectiveEndDate,
                @FinalPayFlag, @BlockingTasksCleared, @EnrollmentSource,
                @CreatedBy, @CreationTimestamp, @LastUpdatedBy, @LastUpdateTimestamp
            )
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            profile.PayrollProfileId,
            profile.EmploymentId,
            profile.PersonId,
            profile.PayrollContextId,
            profile.EnrollmentStatus,
            EffectiveStartDate = profile.EffectiveStartDate.ToDateTime(TimeOnly.MinValue),
            EffectiveEndDate   = profile.EffectiveEndDate?.ToDateTime(TimeOnly.MinValue),
            profile.FinalPayFlag,
            profile.BlockingTasksCleared,
            profile.EnrollmentSource,
            profile.CreatedBy,
            profile.CreationTimestamp,
            profile.LastUpdatedBy,
            profile.LastUpdateTimestamp
        });

        await _auditService.LogAsync(new AuditEventRecord(
            EventType:     "ENROLL",
            EntityType:    "PayrollProfile",
            EntityId:      profile.PayrollProfileId,
            ModuleName:    "PAYROLL",
            ChangeSummary: $"Employment {profile.EmploymentId} enrolled in payroll context {profile.PayrollContextId}",
            AfterJson:     JsonSerializer.Serialize(new
            {
                enrollment_status  = profile.EnrollmentStatus,
                enrollment_source  = profile.EnrollmentSource,
                effective_start_date = profile.EffectiveStartDate.ToString("yyyy-MM-dd")
            })
        ));

        return profile.PayrollProfileId;
    }

    public async Task<PayrollProfile?> GetByEmploymentIdAsync(Guid employmentId)
    {
        const string sql = """
            SELECT * FROM payroll_profile
            WHERE employment_id = @EmploymentId
            """;
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<PayrollProfile>(sql, new { EmploymentId = employmentId });
    }

    public async Task<IReadOnlyList<PayrollProfile>> GetByContextAsync(Guid payrollContextId)
    {
        const string sql = """
            SELECT * FROM payroll_profile
            WHERE payroll_context_id = @PayrollContextId
            ORDER BY effective_start_date DESC
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<PayrollProfile>(sql, new { PayrollContextId = payrollContextId })).ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetActiveEmploymentIdsByContextAsync(Guid payrollContextId)
    {
        const string sql = """
            SELECT employment_id FROM payroll_profile
            WHERE payroll_context_id    = @PayrollContextId
              AND enrollment_status     = 'ACTIVE'
              AND blocking_tasks_cleared = TRUE
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<Guid>(sql, new { PayrollContextId = payrollContextId })).ToList();
    }

    public async Task<int> CountActiveByContextAsync(Guid payrollContextId)
    {
        const string sql = """
            SELECT COUNT(1) FROM payroll_profile
            WHERE payroll_context_id = @PayrollContextId
              AND enrollment_status  = 'ACTIVE'
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (int)await conn.ExecuteScalarAsync<long>(sql, new { PayrollContextId = payrollContextId });
    }

    public async Task<IReadOnlyList<Guid>> GetActiveBlockedEmploymentIdsByContextAsync(Guid payrollContextId)
    {
        const string sql = """
            SELECT employment_id FROM payroll_profile
            WHERE payroll_context_id    = @PayrollContextId
              AND enrollment_status     = 'ACTIVE'
              AND blocking_tasks_cleared = FALSE
            """;
        using var conn = _connectionFactory.CreateConnection();
        return (await conn.QueryAsync<Guid>(sql, new { PayrollContextId = payrollContextId })).ToList();
    }

    public async Task SetBlockingTasksClearedAsync(Guid employmentId, Guid updatedBy)
    {
        const string sql = """
            UPDATE payroll_profile
            SET blocking_tasks_cleared = TRUE,
                last_updated_by        = @UpdatedBy,
                last_update_timestamp  = CURRENT_TIMESTAMP
            WHERE employment_id = @EmploymentId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { EmploymentId = employmentId, UpdatedBy = updatedBy });
    }

    public async Task UpdateStatusAsync(Guid employmentId, string status, Guid updatedBy)
    {
        const string sql = """
            UPDATE payroll_profile
            SET enrollment_status     = @Status,
                last_updated_by       = @UpdatedBy,
                last_update_timestamp = CURRENT_TIMESTAMP
            WHERE employment_id = @EmploymentId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { EmploymentId = employmentId, Status = status, UpdatedBy = updatedBy });

        var eventType = status is "TERMINATED" or "SUSPENDED" ? "DISENROLL" : "STATUS_CHANGE";
        await _auditService.LogAsync(new AuditEventRecord(
            EventType:     eventType,
            EntityType:    "PayrollProfile",
            EntityId:      employmentId,
            ModuleName:    "PAYROLL",
            ChangeSummary: $"Payroll profile status changed to {status} for employment {employmentId}",
            AfterJson:     JsonSerializer.Serialize(new { enrollment_status = status })
        ));
    }

    public async Task SetFinalPayFlagAsync(Guid employmentId, bool finalPayFlag, Guid updatedBy)
    {
        const string sql = """
            UPDATE payroll_profile
            SET final_pay_flag        = @FinalPayFlag,
                enrollment_status     = CASE WHEN @FinalPayFlag THEN 'FINAL_PAY_PENDING' ELSE enrollment_status END,
                last_updated_by       = @UpdatedBy,
                last_update_timestamp = CURRENT_TIMESTAMP
            WHERE employment_id = @EmploymentId
            """;
        using var conn = _connectionFactory.CreateConnection();
        await conn.ExecuteAsync(sql, new { EmploymentId = employmentId, FinalPayFlag = finalPayFlag, UpdatedBy = updatedBy });
    }
}
