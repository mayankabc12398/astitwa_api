using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Repositories;

public sealed class PrintTemplateRepository : RepositoryBase, IPrintTemplateRepository
{
    /// <summary>
    /// camelCase, because the procedure reads the payload with JSON_EXTRACT paths written in
    /// the same spelling the browser sends. One spelling end to end means the designer, the
    /// API and the procedure all name a block's properties identically.
    /// </summary>
    private static readonly JsonSerializerOptions ChildPayload = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PrintTemplateRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<IReadOnlyList<PrintDocumentType>> DocumentTypesAsync(CancellationToken ct = default)
        => QueryAsync<PrintDocumentType>("sp_cfg_print_document_type_list", ct: ct);

    public Task<PagedResult<PrintTemplateListItem>> ListAsync(
        PageRequest page, string? documentType, CancellationToken ct = default)
        => QueryPagedAsync<PrintTemplateListItem>(
            "sp_cfg_print_template_list",
            page,
            ProcArgs.New().Set("document_type", string.IsNullOrWhiteSpace(documentType) ? null : documentType),
            ct);

    public Task<PrintTemplate?> GetAsync(int templateId, CancellationToken ct = default)
        => TreeAsync("sp_cfg_print_template_get", ProcArgs.New().Set("template_id", templateId), ct);

    public Task<PrintTemplate?> ResolveAsync(string documentType, CancellationToken ct = default)
        => TreeAsync("sp_cfg_print_template_resolve", ProcArgs.New().Set("document_type", documentType), ct);

    public Task<IReadOnlyList<PrintAvailableField>> AvailableFieldsAsync(string documentType, CancellationToken ct = default)
        => QueryAsync<PrintAvailableField>(
            "sp_cfg_print_available_field_list",
            ProcArgs.New().Set("document_type", documentType),
            ct);

    public Task<IReadOnlyList<LookupItem>> LookupAsync(string? documentType, CancellationToken ct = default)
        => QueryAsync<LookupItem>(
            "sp_cfg_print_template_lookup",
            ProcArgs.New().Set("document_type", string.IsNullOrWhiteSpace(documentType) ? null : documentType),
            ct);

    public Task<PrintTemplateListItem?> SaveAsync(PrintTemplate template, CancellationToken ct = default)
        => ExecuteReturningAsync<PrintTemplateListItem>(
            "sp_cfg_print_template_save",
            ProcArgs.New()
                .Set("template_id", template.TemplateId)
                .Set("template_code", template.TemplateCode)
                .Set("template_name", template.TemplateName)
                .Set("document_type", template.DocumentType)
                .Set("is_default", template.IsDefault)
                .Set("style_preset", template.StylePreset)
                .Set("page_size", template.PageSize)
                .Set("orientation", template.Orientation)
                .Set("margin_top", template.MarginTop)
                .Set("margin_right", template.MarginRight)
                .Set("margin_bottom", template.MarginBottom)
                .Set("margin_left", template.MarginLeft)
                .Set("font_family", template.FontFamily)
                .Set("font_size_pt", template.FontSizePt)
                .Set("line_height", template.LineHeight)
                .Set("accent_color", template.AccentColor)
                .Set("text_color", template.TextColor)
                .Set("show_logo", template.ShowLogo)
                .Set("logo_url", template.LogoUrl)
                .Set("logo_height_mm", template.LogoHeightMm)
                .Set("logo_align", template.LogoAlign)
                .Set("header_align", template.HeaderAlign)
                .Set("show_header", template.ShowHeader)
                .Set("header_html", template.HeaderHtml)
                .Set("show_footer", template.ShowFooter)
                .Set("footer_html", template.FooterHtml)
                .Set("show_page_numbers", template.ShowPageNumbers)
                .Set("show_watermark", template.ShowWatermark)
                .Set("watermark_text", template.WatermarkText)
                // The whole block list travels as one payload. A block has no stable identity
                // once the designer has reordered it, so the procedure replaces rather than
                // merges — and a merge would have to guess which block a dragged one was.
                .Set("sections_json", JsonSerializer.Serialize(template.Sections, ChildPayload)),
            ct);

    public Task<PrintTemplateListItem?> CloneAsync(int templateId, string templateName, CancellationToken ct = default)
        => ExecuteReturningAsync<PrintTemplateListItem>(
            "sp_cfg_print_template_clone",
            ProcArgs.New().Set("template_id", templateId).Set("template_name", templateName),
            ct);

    public Task<PrintTemplateListItem?> SetDefaultAsync(int templateId, CancellationToken ct = default)
        => ExecuteReturningAsync<PrintTemplateListItem>(
            "sp_cfg_print_template_set_default",
            ProcArgs.New().Set("template_id", templateId),
            ct);

    public async Task<int> DeleteAsync(int templateId, CancellationToken ct = default)
        => await ScalarAsync<int>(
            "sp_cfg_print_template_delete",
            ProcArgs.New().Set("template_id", templateId),
            ct).ConfigureAwait(false);

    /// <summary>
    /// Both the get and the resolve procedure answer with the same three result sets, so the
    /// tree is rebuilt in one place. Fields are matched to blocks by section id rather than
    /// by position, because a block with no fields is legitimate and would shift the pairing.
    /// </summary>
    private async Task<PrintTemplate?> TreeAsync(string procName, ProcArgs args, CancellationToken ct)
    {
        var (templates, sections, fields) =
            await QueryThreeAsync<PrintTemplate, PrintSection, PrintField>(procName, args, ct).ConfigureAwait(false);

        if (templates.Count == 0) return null;

        var template = templates[0];
        var bySection = fields.GroupBy(f => f.SectionId)
                              .ToDictionary(g => g.Key, g => g.OrderBy(f => f.SeqNo).ToList());

        foreach (var section in sections)
        {
            section.Fields = bySection.TryGetValue(section.SectionId, out var own) ? own : new List<PrintField>();
        }

        template.Sections = sections.OrderBy(s => s.SeqNo).ToList();
        return template;
    }
}
