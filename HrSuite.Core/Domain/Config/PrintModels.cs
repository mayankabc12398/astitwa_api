namespace HrSuite.Core.Domain.Config;

/// <summary>
/// What can be templated. Seeded rather than user-created: a document type exists because
/// base code knows how to gather its data, so offering an empty one would advertise a
/// document nobody can print.
/// </summary>
public sealed class PrintDocumentType
{
    public int DocumentTypeId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = "Letter";
    public string LayoutFamily { get; set; } = "Letter";
    public string? ScreenKey { get; set; }
    public string? DefaultTitle { get; set; }
    public bool SupportsTable { get; set; }
    public int SeqNo { get; set; }
    public int TemplateCount { get; set; }
}

/// <summary>List-row projection. Carries the block count so the grid needs no second call.</summary>
public sealed class PrintTemplateListItem
{
    public int TemplateId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsSystem { get; set; }
    public string PageSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public string StylePreset { get; set; } = "Letter";
    public int Version { get; set; }
    public int SectionCount { get; set; }
    public DateTime? UpdatedOn { get; set; }
}

/// <summary>
/// The page-level design. Everything here is a look, not a rule: two tenants printing the
/// same document type differ only by these rows, which is the whole point of the screen.
/// </summary>
public sealed class PrintTemplate
{
    public int TemplateId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsSystem { get; set; }

    /// <summary>Caption the renderer falls back to, taken from the document catalogue.</summary>
    public string? DocumentTitle { get; set; }

    public string StylePreset { get; set; } = "Letter";

    public string PageSize { get; set; } = "A4";
    public string Orientation { get; set; } = "portrait";
    public decimal MarginTop { get; set; } = 14m;
    public decimal MarginRight { get; set; } = 14m;
    public decimal MarginBottom { get; set; } = 14m;
    public decimal MarginLeft { get; set; } = 14m;

    public string FontFamily { get; set; } = "Segoe UI";
    public decimal FontSizePt { get; set; } = 10.5m;
    public decimal LineHeight { get; set; } = 1.45m;
    public string AccentColor { get; set; } = "#4f46e5";
    public string TextColor { get; set; } = "#1f2937";

    public bool ShowLogo { get; set; } = true;
    public string? LogoUrl { get; set; }
    public decimal LogoHeightMm { get; set; } = 14m;
    public string LogoAlign { get; set; } = "left";
    public string? HeaderAlign { get; set; }
    public bool ShowHeader { get; set; } = true;
    public string? HeaderHtml { get; set; }
    public bool ShowFooter { get; set; } = true;
    public string? FooterHtml { get; set; }
    public bool ShowPageNumbers { get; set; } = true;
    public bool ShowWatermark { get; set; }
    public string? WatermarkText { get; set; }

    public int Version { get; set; } = 1;

    /// <summary>
    /// Empty is meaningful: the renderer treats a template with no blocks as "auto" and
    /// prints the whole data context, so a freshly seeded template is never a blank page.
    /// </summary>
    public List<PrintSection> Sections { get; set; } = new();
}

public sealed class PrintSection
{
    public int SectionId { get; set; }
    public int TemplateId { get; set; }
    public string SectionType { get; set; } = "FieldGrid";
    public int SeqNo { get; set; }
    public string? Title { get; set; }
    public int ColumnCount { get; set; } = 2;
    public string BorderStyle { get; set; } = "none";
    public string? BorderColor { get; set; }
    public string? BackgroundColor { get; set; }
    public decimal PaddingMm { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? ConfigJson { get; set; }
    public List<PrintField> Fields { get; set; } = new();
}

public sealed class PrintField
{
    public int TemplateFieldId { get; set; }
    public int SectionId { get; set; }

    /// <summary>An employee key, a custom field key, or the literal '@static'.</summary>
    public string FieldKey { get; set; } = string.Empty;

    public string? Label { get; set; }
    public int SeqNo { get; set; }
    public int WidthPercent { get; set; } = 50;
    public string Align { get; set; } = "left";
    public bool IsBold { get; set; }
    public string Format { get; set; } = "text";
    public bool ShowLabel { get; set; } = true;
    public string? StaticText { get; set; }
}

/// <summary>
/// What the designer may drop into a block. Origin tells the author where a key comes from,
/// which matters because a Custom key stops resolving the moment its field is deleted.
/// </summary>
public sealed class PrintAvailableField
{
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Origin { get; set; } = "Base";      // Base | Context | Custom
    public string ControlType { get; set; } = "text";
    public string SuggestedFormat { get; set; } = "text";
    public int SeqNo { get; set; }
}

/// <summary>A clone request. The name is required; everything else is copied.</summary>
public sealed class PrintTemplateCloneRequest
{
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
}
