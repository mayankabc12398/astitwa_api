using HrSuite.Core.Abstractions;
using HrSuite.Core.Extensibility;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Extensions.Engine.Runtime;
using Xunit;

namespace HrSuite.ArchitectureTests;

/// <summary>
/// Behavioural tests for the Layer 5 sandbox.
///
/// They live in this project because it is the only test assembly the solution defines, and
/// because the guarantees they cover are architectural: a script must not be able to reach
/// the host, and a broken script must never block a save (section 10.5).
/// </summary>
public class ScriptSandboxTests
{
    private static ScriptHost Host(INamedQueryRunner? queries = null)
        => new(queries ?? new StubQueryRunner());

    private static HookContext Context(object? form = null, object? value = null) => new()
    {
        HookKey = "hr.employee.beforeSave",
        Form = form as IDictionary<string, object?> ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
        Value = value,
        User = new HookUser { Id = 7, Name = "tester", Roles = new[] { "ADMIN" } },
        Tenant = new HookTenant { Id = 1, Code = "ACME" }
    };

    [Fact]
    public void A_script_can_return_calculated_fields_in_form()
    {
        // The contract has always advertised `return { form: { ... } }`, but the result was
        // built from ctx.form alone — so a script could compute a value, return it, be
        // reported ok, and have the answer silently dropped. Nothing said so; the field just
        // stayed empty. This is the guard.
        var form = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["grossCtc"] = "1000000"
        };

        var outcome = Host().Run(
            "var gross = Number(ctx.form.grossCtc) || 0;" +
            "var tds = Math.round(gross * 0.1 * 100) / 100;" +
            "return { form: { tds: tds, netSalary: gross - tds } };",
            Context(form), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.NotNull(outcome.Result.Form);
        Assert.Equal("100000", outcome.Result.Form!["tds"]?.ToString());
        Assert.Equal("900000", outcome.Result.Form!["netSalary"]?.ToString());

        // Untouched fields survive: a returned form adds to the record, it does not replace it.
        Assert.Equal("1000000", outcome.Result.Form!["grossCtc"]?.ToString());
    }

    [Fact]
    public void A_returned_form_wins_over_the_same_field_set_with_setForm()
    {
        var outcome = Host().Run(
            "ctx.setForm('hra', 1); return { form: { hra: 2 } };",
            Context(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal("2", outcome.Result.Form!["hra"]?.ToString());
    }

    [Fact]
    public void An_empty_script_returns_an_empty_result()
    {
        var outcome = Host().Run("", Context(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.True(outcome.Result.IsEmpty);
    }

    [Fact]
    public void A_script_can_cancel_a_save_with_a_message()
    {
        var form = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["dateOfJoining"] = "2099-01-01"
        };

        const string body = """
            var doj = new Date(ctx.form.dateOfJoining);
            var today = new Date();
            if (doj > today) return { cancelSave: true, message: 'Joining date is in the future.' };
            """;

        var outcome = Host().Run(body, Context(form), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.True(outcome.Result.CancelSave);
        Assert.Equal("Joining date is in the future.", outcome.Result.Message);
    }

    [Fact]
    public void A_syntax_error_is_reported_and_never_thrown()
    {
        // Acceptance scenario 4: this is what a broken script must do.
        var outcome = Host().Run("if (utils.isEmpty(x return;", Context(), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Error));
        Assert.True(outcome.Result.IsEmpty); // the caller sees "no hook registered"
    }

    [Fact]
    public void A_runtime_error_is_reported_and_never_thrown()
    {
        var outcome = Host().Run("throw new Error('boom');", Context(), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("boom", outcome.Error);
        Assert.True(outcome.Result.IsEmpty);
    }

    [Fact]
    public void An_endless_loop_is_stopped_by_the_timeout()
    {
        var outcome = Host().Run("while (true) { }", Context(), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.True(outcome.TimedOut);
        Assert.True(outcome.DurationMs < 15_000, "The timeout must fire well inside a request budget.");
    }

    [Fact]
    public void Setform_edits_come_back_to_the_host()
    {
        var form = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["mobile"] = " 98765 " };

        var outcome = Host().Run("ctx.setForm('mobile', String(ctx.form.mobile).trim());", Context(form), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.NotNull(outcome.Result.Form);
        Assert.Equal("98765", outcome.Result.Form!["mobile"]?.ToString());
    }

    [Fact]
    public async Task Await_works_the_same_as_it_does_in_the_browser_sandbox()
    {
        await Task.CompletedTask;

        const string body = """
            var result = await api.query('hr.employee.searchByMobile', { mobile: '98765' });
            if (!result.ok) return { message: 'query failed' };
            return { message: 'rows=' + result.rows.length };
            """;

        var outcome = Host(new StubQueryRunner(rows: 2)).Run(body, Context(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal("rows=2", outcome.Result.Message);
    }

    [Fact]
    public void A_script_cannot_reach_the_clr()
    {
        // CLR interop is never switched on, so this is a plain reference error.
        var outcome = Host().Run("return { message: System.IO.File.ReadAllText('C:/Windows/win.ini') };",
            Context(), CancellationToken.None);

        Assert.False(outcome.Ok);
    }

    [Theory]
    [InlineData("return { message: typeof window };", "undefined")]
    [InlineData("return { message: typeof document };", "undefined")]
    [InlineData("return { message: typeof fetch };", "undefined")]
    [InlineData("return { message: typeof require };", "undefined")]
    [InlineData("return { message: typeof process };", "undefined")]
    [InlineData("return { message: typeof __query };", "undefined")]
    [InlineData("return { message: typeof __record };", "undefined")]
    public void The_only_doors_out_are_the_four_objects(string body, string expected)
    {
        var outcome = Host().Run(body, Context(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(expected, outcome.Result.Message);
    }

    [Theory]
    [InlineData("return { message: typeof ctx };", "object")]
    [InlineData("return { message: typeof api.query };", "function")]
    [InlineData("return { message: typeof ui.pickList };", "function")]
    [InlineData("return { message: typeof utils.age };", "function")]
    public void The_four_objects_are_present(string body, string expected)
    {
        var outcome = Host().Run(body, Context(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(expected, outcome.Result.Message);
    }

    [Fact]
    public void Utils_age_is_calendar_correct()
    {
        var outcome = Host().Run(
            "return { message: String(utils.age('2000-06-15', '2020-06-14')) };",
            Context(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal("19", outcome.Result.Message);
    }

    [Fact]
    public void A_script_cannot_widen_its_own_api_surface()
    {
        // api and ui are frozen, so a script cannot bolt an extra method onto them and
        // have a later script find it.
        var outcome = Host().Run(
            "api.escape = function () { return 1; }; return { message: String(typeof api.escape) };",
            Context(), CancellationToken.None);

        // Strict mode turns the assignment into a TypeError rather than a silent no-op.
        Assert.False(outcome.Ok);
    }

    // api.callEndpoint() reads the same on the server as in the browser sandbox. The Test
    // button runs every script here, including client ones, so a client script that calls
    // an endpoint must at least parse and run — otherwise it can never be saved.
    [Fact]
    public void A_script_can_call_an_api_builder_endpoint()
    {
        const string body = """
            var res = await api.callEndpoint('getnextuhid', {});
            if (!res.ok) return { message: 'failed: ' + res.error };
            return { form: { patientCode: res.rows[0].uhid } };
            """;

        var host = new ScriptHost(new StubQueryRunner(), new StubEndpointCaller("getnextuhid", "UH-000042"));
        var outcome = host.Run(body, Context(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal("UH-000042", outcome.Result.Form?["patientCode"]?.ToString());
    }

    [Fact]
    public void Without_an_endpoint_caller_callEndpoint_answers_not_ok_rather_than_throwing()
    {
        const string body = """
            var res = await api.callEndpoint('getnextuhid');
            return { message: res.ok ? 'ok' : 'not ok' };
            """;

        var outcome = Host().Run(body, Context(), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal("not ok", outcome.Result.Message);
    }

    private sealed class StubEndpointCaller : ICustomApiCaller
    {
        private readonly string _slug;
        private readonly string _uhid;

        public StubEndpointCaller(string slug, string uhid)
        {
            _slug = slug;
            _uhid = uhid;
        }

        public Task<CustomApiResult> RunAsync(string slug, IDictionary<string, object?>? supplied, CancellationToken ct = default)
        {
            if (!string.Equals(slug, _slug, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CustomApiResult.Failure($"'{slug}' is not an endpoint."));

            var rows = new List<IDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["uhid"] = _uhid }
            };

            return Task.FromResult(new CustomApiResult(true, rows, new[] { "uhid" }, false));
        }
    }

    private sealed class StubQueryRunner : INamedQueryRunner
    {
        private readonly int _rows;

        public StubQueryRunner(int rows = 0) => _rows = rows;

        public Task<NamedQueryResult> RunAsync(
            string queryKey, IDictionary<string, object?>? parameters, CancellationToken ct = default)
        {
            var rows = Enumerable.Range(1, _rows)
                .Select(i => (IDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["employee_id"] = i,
                    ["full_name"] = $"Person {i}"
                })
                .ToList();

            return Task.FromResult(new NamedQueryResult(true, rows, new[] { "employee_id", "full_name" }, false));
        }
    }
}
