using HrSuite.Common.Results;
using HrSuite.Infrastructure.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HrSuite.ArchitectureTests;

/// <summary>
/// Section 11: every list endpoint is paged, and the page the caller asked for is the page
/// the caller gets.
///
/// These exist because that convention was quietly broken and nothing noticed. Every list
/// action declares <c>PageRequest page</c>; the default complex-type binder took the
/// parameter name as a prefix, saw <c>?page=1</c> as a match for it, then looked for
/// <c>page.pageSize</c>, found nothing, and handed the action a DEFAULT PageRequest. The
/// endpoint answered 200 with the first 25 rows no matter what was asked for.
///
/// It only misbehaved when BOTH keys were sent — <c>?pageSize=3</c> on its own worked — which
/// is exactly why a casual check missed it. The binder is the fix; these are the guard.
/// </summary>
public sealed class PagingConventionTests
{
    private static PageRequest Bind(string queryString)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString(queryString);

        var context = new DefaultModelBindingContext
        {
            ActionContext = new ActionContext { HttpContext = http },
            ModelState = new ModelStateDictionary(),
            ModelName = "page"   // the name that used to poison the binding
        };

        new PageRequestModelBinder().BindModelAsync(context).GetAwaiter().GetResult();

        Assert.True(context.Result.IsModelSet);
        return Assert.IsType<PageRequest>(context.Result.Model);
    }

    [Fact]
    public void Page_and_page_size_are_both_honoured_when_both_are_sent()
    {
        var bound = Bind("?page=3&pageSize=10");

        Assert.Equal(3, bound.Page);
        Assert.Equal(10, bound.PageSize);
        Assert.Equal(20, bound.Offset);
    }

    [Fact]
    public void Page_size_alone_is_honoured()
    {
        var bound = Bind("?pageSize=7");

        Assert.Equal(1, bound.Page);
        Assert.Equal(7, bound.PageSize);
    }

    [Fact]
    public void Search_is_trimmed_and_blank_becomes_null()
    {
        Assert.Equal("nair", Bind("?search=%20nair%20").Search);
        Assert.Null(Bind("?search=%20%20").Search);
        Assert.Null(Bind("?page=1").Search);
    }

    [Fact]
    public void An_unbounded_page_size_is_clamped_rather_than_obeyed()
    {
        Assert.Equal(PageRequest.MaxPageSize, Bind("?page=1&pageSize=100000").PageSize);
    }

    [Fact]
    public void Nonsense_falls_back_to_the_first_page_rather_than_failing()
    {
        var bound = Bind("?page=abc&pageSize=-4");

        Assert.Equal(1, bound.Page);
        Assert.Equal(25, bound.PageSize);
    }

}
