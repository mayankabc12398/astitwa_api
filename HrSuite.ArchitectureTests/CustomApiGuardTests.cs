using HrSuite.Extensions.Engine.Models;
using HrSuite.Extensions.Engine.Runtime;
using Xunit;

namespace HrSuite.ArchitectureTests;

/// <summary>
/// SqlGuard is what stands between "an administrator may write a query" and "an
/// administrator may do anything to this database". Every rule it enforces has a test
/// here, because the rules are only worth what they are worth when nobody has quietly
/// loosened one.
/// </summary>
public class CustomApiGuardTests
{
    private static readonly CustomApiParam[] NoParams = Array.Empty<CustomApiParam>();

    private const string Valid = "SELECT full_name FROM hr_employee WHERE tenant_id = {tenant}";

    [Fact]
    public void A_plain_select_with_the_tenant_token_is_allowed()
        => Assert.True(SqlGuard.Check(Valid, NoParams).Ok);

    [Fact]
    public void A_common_table_expression_is_allowed()
    {
        const string sql =
            "WITH active AS (SELECT employee_id, tenant_id FROM hr_employee WHERE is_active = 1) " +
            "SELECT employee_id FROM active WHERE tenant_id = {tenant}";

        Assert.True(SqlGuard.Check(sql, NoParams).Ok);
    }

    [Theory]
    [InlineData("UPDATE hr_employee SET full_name = 'x' WHERE tenant_id = {tenant}")]
    [InlineData("DELETE FROM hr_employee WHERE tenant_id = {tenant}")]
    [InlineData("DROP TABLE hr_employee")]
    [InlineData("CALL sp_hr_employee_list(1, 1, NULL, 10, 0)")]
    public void A_statement_that_is_not_a_read_is_refused(string sql)
        => Assert.False(SqlGuard.Check(sql, NoParams).Ok);

    [Fact]
    public void A_second_statement_is_refused()
        => Assert.False(SqlGuard.Check(Valid + "; DROP TABLE hr_employee", NoParams).Ok);

    [Theory]
    [InlineData(" -- and more")]
    [InlineData(" # and more")]
    [InlineData(" /* and more */")]
    public void A_comment_is_refused(string tail)
        => Assert.False(SqlGuard.Check(Valid + tail, NoParams).Ok);

    [Fact]
    public void Sql_without_the_tenant_token_is_refused()
        => Assert.False(SqlGuard.Check("SELECT full_name FROM hr_employee", NoParams).Ok);

    [Theory]
    [InlineData("SELECT table_name FROM information_schema.tables WHERE 1 = {tenant}")]
    [InlineData("SELECT user FROM mysql.user WHERE 1 = {tenant}")]
    public void The_server_s_own_schemas_are_out_of_reach(string sql)
        => Assert.False(SqlGuard.Check(sql, NoParams).Ok);

    [Fact]
    public void Writing_a_file_is_refused()
        => Assert.False(SqlGuard.Check(Valid + " INTO OUTFILE '/tmp/x'", NoParams).Ok);

    [Fact]
    public void A_placeholder_with_no_declaration_is_refused()
        => Assert.False(SqlGuard.Check(Valid + " AND department_id = @dept", NoParams).Ok);

    [Fact]
    public void A_declaration_with_no_placeholder_is_refused()
    {
        var declared = new[] { new CustomApiParam { Name = "dept", Type = "int" } };
        Assert.False(SqlGuard.Check(Valid, declared).Ok);
    }

    [Fact]
    public void A_declared_placeholder_that_is_used_is_allowed()
    {
        var declared = new[] { new CustomApiParam { Name = "dept", Type = "int", Required = true } };
        Assert.True(SqlGuard.Check(Valid + " AND department_id = @dept", declared).Ok);
    }

    [Fact]
    public void The_runner_s_own_parameters_cannot_be_declared_or_used()
    {
        var declared = new[] { new CustomApiParam { Name = SqlGuard.TenantParam, Type = "int" } };
        Assert.False(SqlGuard.Check(Valid, declared).Ok);
        Assert.False(SqlGuard.Check($"SELECT 1 FROM hr_employee WHERE tenant_id = @{SqlGuard.TenantParam}", NoParams).Ok);
    }

    [Fact]
    public void An_unknown_parameter_type_is_refused()
    {
        var declared = new[] { new CustomApiParam { Name = "dept", Type = "object" } };
        Assert.False(SqlGuard.Check(Valid + " AND department_id = @dept", declared).Ok);
    }

    [Fact]
    public void Compiling_replaces_the_token_with_a_bound_parameter_and_caps_the_rows()
    {
        var compiled = SqlGuard.Compile(Valid);

        Assert.DoesNotContain(SqlGuard.TenantToken, compiled);
        Assert.Contains("@" + SqlGuard.TenantParam, compiled);
        Assert.Contains("LIMIT @" + SqlGuard.MaxRowsParam, compiled);
    }

    [Theory]
    [InlineData("employees-by-department", true)]
    [InlineData("Employees", false)]           // an upper-case URL is a different URL
    [InlineData("-leading-hyphen", false)]
    [InlineData("has space", false)]
    [InlineData("a", false)]                   // one character is not a name
    public void A_slug_is_held_to_what_a_url_segment_may_be(string slug, bool expected)
        => Assert.Equal(expected, SqlGuard.IsValidSlug(slug));
}
