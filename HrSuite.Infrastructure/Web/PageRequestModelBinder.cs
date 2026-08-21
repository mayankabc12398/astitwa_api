using HrSuite.Common.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HrSuite.Infrastructure.Web;

/// <summary>
/// Binds <see cref="PageRequest"/> straight from the query string, ignoring the name of the
/// action parameter it is bound to.
///
/// Why this exists rather than an attribute at every call site:
///
/// The default complex-type binder derives its prefix from the parameter name. Every list
/// action here declares <c>PageRequest page</c>, and every caller sends <c>?page=1</c> — so
/// the binder sees a query key matching the prefix, then looks for <c>page.pageSize</c>,
/// finds nothing, and hands the action a request with the DEFAULT page size. Paging is
/// silently ignored, and only when both keys are sent: <c>?pageSize=3</c> alone works, which
/// is what makes it easy to miss.
///
/// Nine list endpoints across layers 1, 3 and 5 had that shape. Fixing each with
/// <c>[FromQuery(Name = "")]</c> would leave the tenth free to get it wrong, so the rule is
/// attached to the type. A plugin's controllers are bound by the host's MVC options too, so
/// an add-on written later inherits the fix without knowing it exists.
/// </summary>
public sealed class PageRequestModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var query = bindingContext.HttpContext.Request.Query;
        var model = new PageRequest();

        // The setters clamp: page below 1 becomes 1, page size is bounded by MaxPageSize.
        // Unparseable text is left as the default rather than rejected — a bad ?page=abc
        // should show the first page, not a 400.
        if (int.TryParse(query["page"], out var page)) model.Page = page;
        if (int.TryParse(query["pageSize"], out var pageSize)) model.PageSize = pageSize;

        var search = query["search"].ToString();
        model.Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        bindingContext.Result = ModelBindingResult.Success(model);
        return Task.CompletedTask;
    }
}

/// <summary>Hands <see cref="PageRequest"/> to the binder above, and nothing else.</summary>
public sealed class PageRequestModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Metadata.ModelType == typeof(PageRequest)
            ? new PageRequestModelBinder()
            : null;
    }
}
