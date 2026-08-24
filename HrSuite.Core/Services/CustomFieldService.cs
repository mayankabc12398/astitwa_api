using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Extensibility;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

/// <summary>
/// Layer 1 rules for a tenant-defined field.
///
/// The screens that accept one come from <see cref="ScreenCatalog"/> rather than from a
/// table, for the same reason the hook editor reads it: the set of screens is a fact about
/// the compiled application, and a registry row could name a screen that does not exist.
///
/// A custom field key may not collide with a compiled one. If it did, cfg_field_rule and
/// this table would both claim the same key on the same screen and the form would render
/// the field twice.
///
/// Values are validated here, not merely stored. The column is TEXT for every control type,
/// so "is this actually a number" has to be answered before the write rather than discovered
/// by whatever reads it later.
/// </summary>
public sealed class CustomFieldService : ICustomFieldService
{
    private static readonly IReadOnlyList<CustomControlType> Controls = new[]
    {
        new CustomControlType { ControlType = "text",     Label = "Text" },
        new CustomControlType { ControlType = "textarea", Label = "Paragraph" },
        new CustomControlType { ControlType = "number",   Label = "Whole number", IsNumeric = true },
        new CustomControlType { ControlType = "decimal",  Label = "Decimal",      IsNumeric = true },
        new CustomControlType { ControlType = "date",     Label = "Date",         IsDate = true },
        new CustomControlType { ControlType = "datetime", Label = "Date and time", IsDate = true },
        new CustomControlType { ControlType = "checkbox", Label = "Yes / no" },
        new CustomControlType { ControlType = "dropdown", Label = "Dropdown",     HasOptions = true },
        new CustomControlType { ControlType = "radio",    Label = "Radio buttons", HasOptions = true }
    };

    /// <summary>
    /// The lookups a dropdown may bind to. An allowlist, not a free URL: a field must not be
    /// able to name an endpoint, which is what turns a configuration screen into an SSRF.
    /// </summary>
    private static readonly HashSet<string> LookupKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "department", "designation", "employee", "leaveType"
    };

    private static readonly HashSet<string> Widths = new(StringComparer.OrdinalIgnoreCase) { "half", "full" };

    private static readonly HashSet<string> DataSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "None", "Static", "Lookup", "Dynamic"
    };

    private static readonly HashSet<string> ValueModes = new(StringComparer.OrdinalIgnoreCase) { "Manual", "Computed" };

    private static readonly HashSet<string> RecalcModes = new(StringComparer.OrdinalIgnoreCase) { "Always", "Prefill" };

    /// <summary>A JavaScript-safe identifier: it becomes a key on the form object.</summary>
    private static readonly Regex KeyShape = new("^[a-z][a-zA-Z0-9]{1,79}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions AuditShape = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ICustomFieldRepository _repository;
    private readonly INamedQueryRunner _namedQueries;

    public CustomFieldService(ICustomFieldRepository repository, INamedQueryRunner namedQueries)
    {
        _repository = repository;
        _namedQueries = namedQueries;
    }

    public async Task<IReadOnlyList<CustomFieldScreen>> ScreensAsync(CancellationToken ct = default)
    {
        var configured = await _repository.ListAsync(null, ct).ConfigureAwait(false);

        return ScreenCatalog.Screens
            .Select(s => new CustomFieldScreen
            {
                ScreenKey = s.Key,
                Label = s.Label,
                FieldCount = configured.Count(f => string.Equals(f.ScreenKey, s.Key, StringComparison.OrdinalIgnoreCase)),
                CompiledFieldKeys = s.Fields.Select(f => f.Key).ToList(),
                // A screen that has not declared positions still gets usable ones: its
                // fields are numbered by the order they are catalogued in, on the same ten
                // spacing the sectioned screens use, so "place after" works everywhere.
                CompiledFields = s.Fields
                    .Select((f, index) => new CompiledScreenField
                    {
                        FieldKey = f.Key,
                        Label = f.Label,
                        SectionKey = f.Section,
                        SeqNo = f.Seq > 0 ? f.Seq : (index + 1) * 10
                    })
                    .OrderBy(f => f.SeqNo)
                    .ToList(),
                Sections = (s.Sections ?? Array.Empty<ScreenCatalog.ScreenSection>())
                    .Select(section => new CustomFieldSection
                    {
                        SectionKey = section.Key,
                        Label = section.Label
                    })
                    .ToList()
            })
            .ToList();
    }

    public IReadOnlyList<CustomControlType> ControlTypes() => Controls;

    public Task<IReadOnlyList<CustomField>> ListAsync(string screenKey, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(screenKey)
            ? Task.FromResult<IReadOnlyList<CustomField>>(Array.Empty<CustomField>())
            : _repository.ListAsync(screenKey.Trim(), ct);

    public async Task<Result<CustomField>> GetAsync(int fieldId, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(fieldId, ct).ConfigureAwait(false);
        return found is null ? Result<CustomField>.NotFound("Field not found.") : Result<CustomField>.Success(found);
    }

    public async Task<Result<CustomField>> SaveAsync(CustomField field, CancellationToken ct = default)
    {
        var validation = new Validator()
            .RequireText(field.ScreenKey, "Screen is required.", "screenKey")
            .RequireText(field.Label, "Label is required.", "label")
            .Require(Controls.Any(c => string.Equals(c.ControlType, field.ControlType, StringComparison.OrdinalIgnoreCase)),
                     "That control type is not one this product renders.", "controlType")
            .ToResult();

        if (validation.IsFailure) return Result<CustomField>.Fail(validation.Errors.ToArray());

        field.ScreenKey = field.ScreenKey.Trim();
        field.Label = field.Label.Trim();
        field.ControlType = field.ControlType.Trim().ToLowerInvariant();
        field.Width = Widths.Contains(field.Width) ? field.Width.ToLowerInvariant() : "half";
        field.DataSourceType = DataSources.Contains(field.DataSourceType) ? field.DataSourceType : "None";

        var screen = ScreenCatalog.Screens.FirstOrDefault(
            s => string.Equals(s.Key, field.ScreenKey, StringComparison.OrdinalIgnoreCase));

        if (screen is null)
        {
            return Result<CustomField>.Invalid(
                $"'{field.ScreenKey}' is not a screen that accepts extra fields.", "screenKey");
        }

        var isNew = field.FieldId == 0;
        var before = isNew ? null : await _repository.GetAsync(field.FieldId, ct).ConfigureAwait(false);

        var keyCheck = await ResolveKeyAsync(field, screen, ct).ConfigureAwait(false);
        if (keyCheck.IsFailure) return Result<CustomField>.Fail(keyCheck.Errors.ToArray());

        var source = await ValidateSourceAsync(field, ct).ConfigureAwait(false);
        if (source.IsFailure) return Result<CustomField>.Fail(source.Errors.ToArray());

        var formula = await ValidateFormulaAsync(field, ct).ConfigureAwait(false);
        if (formula.IsFailure) return Result<CustomField>.Fail(formula.Errors.ToArray());

        if (field.SeqNo <= 0) field.SeqNo = 1000;

        CustomField? saved;
        try
        {
            saved = await _repository.SaveAsync(field, ct).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            // The unique index closes the race a pre-check cannot: two admins adding the
            // same field key at once.
            return Result<CustomField>.Fail(
                Error.Validation($"A field with the key '{field.FieldKey}' already exists on this screen.", "fieldKey"));
        }

        if (saved is null) return Result<CustomField>.NotFound("Field not found.");

        // The binding lives in its own table, so it is written after the field has an id.
        // A field that is no longer Dynamic loses its binding rather than keeping a stale
        // one that nothing reads but everything would still see.
        if (string.Equals(field.DataSourceType, "Dynamic", StringComparison.OrdinalIgnoreCase) && field.Binding is not null)
        {
            await _repository.SaveBindingAsync(saved.FieldId, field.Binding, ct).ConfigureAwait(false);
        }
        else
        {
            await _repository.DeleteBindingAsync(saved.FieldId, ct).ConfigureAwait(false);
        }

        // The save procedure answers with the field row alone, so the echo would carry an
        // empty option list while a read of the same field carries a full one. Re-reading
        // costs one round trip on a rare administrative write and keeps the two shapes
        // identical, which is what lets a caller trust what it just posted.
        var reloaded = await _repository.GetAsync(saved.FieldId, ct).ConfigureAwait(false);
        var result = reloaded ?? saved;

        await AuditAsync(field.ScreenKey, result.FieldId, result.FieldKey, isNew ? "ADD" : "UPDATE", before, result, ct)
            .ConfigureAwait(false);

        return Result<CustomField>.Success(result);
    }

    public async Task<Result> DeleteAsync(int fieldId, CancellationToken ct = default)
    {
        var existing = await _repository.GetAsync(fieldId, ct).ConfigureAwait(false);
        if (existing is null) return Result.Fail(Error.NotFound("Field not found."));

        // What people typed is copied to the archive first, so a value survives the layout
        // decision that removed the field showing it. The live rows stay too: a field
        // re-added with the same key would otherwise come back empty.
        await _repository.ArchiveValuesAsync(fieldId, ct).ConfigureAwait(false);

        await _repository.DeleteAsync(fieldId, ct).ConfigureAwait(false);

        await AuditAsync(existing.ScreenKey, fieldId, existing.FieldKey, "DELETE", existing, null, ct)
            .ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> ReorderAsync(IReadOnlyList<CustomFieldOrderEntry> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return Result.Success();

        await _repository.ReorderAsync(items, ct).ConfigureAwait(false);

        // One record for the whole batch: a drag moves several fields at once, and a row
        // per field would bury the change that actually happened.
        await AuditAsync(null, 0, null, "REORDER", null, items, ct).ConfigureAwait(false);

        return Result.Success();
    }

    public Task<CustomFieldUsage> UsageAsync(int fieldId, CancellationToken ct = default)
        => _repository.UsageAsync(fieldId, ct);

    public Task<IReadOnlyList<CustomValue>> ValuesAsync(string screenKey, int recordId, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(screenKey) || recordId <= 0
            ? Task.FromResult<IReadOnlyList<CustomValue>>(Array.Empty<CustomValue>())
            : _repository.ValuesAsync(screenKey.Trim(), recordId, ct);

    public async Task<Result> SaveValuesAsync(CustomValueSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ScreenKey)) return Result.Invalid("Screen is required.", "screenKey");
        if (request.RecordId <= 0) return Result.Invalid("The record must be saved before its extra fields.", "recordId");

        var fields = await _repository.ListAsync(request.ScreenKey.Trim(), ct).ConfigureAwait(false);
        if (fields.Count == 0) return Result.Success();

        var posted = request.Values.ToDictionary(v => v.FieldKey, v => v.ValueText, StringComparer.OrdinalIgnoreCase);

        // Computed values are derived before anything is validated, so what gets checked and
        // stored is what the formula produced rather than whatever the browser sent. The
        // browser runs the same grammar for immediacy; this is the answer that is kept.
        Recalculate(fields, posted);

        var errors = new Validator();
        var writes = new List<CustomValueEntry>();

        foreach (var field in fields)
        {
            // A field the tenant hid from the form is not the poster's to fill, so a
            // payload that omits it is correct rather than incomplete.
            if (!field.ShowInForm) continue;

            posted.TryGetValue(field.FieldKey, out var raw);
            var value = raw?.Trim();

            if (string.IsNullOrEmpty(value))
            {
                if (field.IsRequired) errors.Require(false, $"{field.Label} is required.", field.FieldKey);
                writes.Add(new CustomValueEntry { FieldKey = field.FieldKey, ValueText = null });
                continue;
            }

            var coerced = Coerce(field, value, errors);
            writes.Add(new CustomValueEntry { FieldKey = field.FieldKey, ValueText = coerced });
        }

        if (errors.HasErrors) return errors.ToResult();

        await _repository.SaveValuesAsync(request.ScreenKey.Trim(), request.RecordId, writes, ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>
    /// Fills in every computed field, in an order where each is evaluated after the fields
    /// it reads â€” so a formula that depends on another formula sees the derived value and
    /// not the blank it started as.
    ///
    /// Always overwrites whatever was posted, which is why its control is read-only.
    /// Prefill only fills a blank, so a suggestion can be overridden.
    ///
    /// A formula that cannot be evaluated leaves its field alone rather than failing the
    /// save: the record is the user's work, and a configuration mistake in a derived field
    /// must not be the reason they lose it.
    /// </summary>
    private static void Recalculate(IReadOnlyList<CustomField> fields, Dictionary<string, string?> values)
    {
        var computed = fields
            .Where(f => string.Equals(f.ValueMode, "Computed", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(f.FormulaText))
            .ToList();

        if (computed.Count == 0) return;

        var ordered = FormulaEngine.InEvaluationOrder(
            computed,
            f => f.FieldKey,
            f => string.IsNullOrWhiteSpace(f.FormulaRefsCsv)
                ? FormulaEngine.ExtractRefs(f.FormulaText)
                : f.FormulaRefsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var field in ordered)
        {
            var prefillOnly = string.Equals(field.RecalcMode, "Prefill", StringComparison.OrdinalIgnoreCase);
            if (prefillOnly && values.TryGetValue(field.FieldKey, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                continue;
            }

            var result = FormulaEngine.Evaluate(field.FormulaText, values, field.RoundTo);
            if (!result.Ok) continue;

            values[field.FieldKey] = result.Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// A new field gets a key derived from its label; an existing one keeps the key it has.
    /// The key is fixed on purpose: a print template and a stored script both reference a
    /// field by name, and renaming it would break them without a single error anywhere.
    /// </summary>
    private async Task<Result> ResolveKeyAsync(CustomField field, ScreenCatalog.Screen screen, CancellationToken ct)
    {
        if (field.FieldId > 0)
        {
            var existing = await _repository.GetAsync(field.FieldId, ct).ConfigureAwait(false);
            if (existing is null) return Result.Fail(Error.NotFound("Field not found."));

            field.FieldKey = existing.FieldKey;
            field.ScreenKey = existing.ScreenKey;
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(field.FieldKey)) field.FieldKey = KeyFrom(field.Label);
        field.FieldKey = field.FieldKey.Trim();

        if (!KeyShape.IsMatch(field.FieldKey))
        {
            return Result.Invalid(
                "Field key must start with a lower-case letter and contain only letters and digits.", "fieldKey");
        }

        if (screen.Fields.Any(f => string.Equals(f.Key, field.FieldKey, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Invalid(
                $"'{field.FieldKey}' is already a field on this screen. Choose another name.", "fieldKey");
        }

        return Result.Success();
    }

    private async Task<Result> ValidateSourceAsync(CustomField field, CancellationToken ct)
    {
        var control = Controls.First(c => string.Equals(c.ControlType, field.ControlType, StringComparison.OrdinalIgnoreCase));

        if (!control.HasOptions)
        {
            // A text box with a leftover option list would keep answering with choices
            // nothing renders, so the switch clears them here as well as in the procedure.
            field.DataSourceType = "None";
            field.Options.Clear();
            field.LookupKey = null;
            field.ParentFieldKey = null;
            field.Binding = null;
            return Result.Success();
        }

        if (string.Equals(field.DataSourceType, "Dynamic", StringComparison.OrdinalIgnoreCase))
        {
            if (field.Binding is null || field.Binding.SourceId <= 0)
            {
                return Result.Invalid("Choose the source this list reads from.", "binding");
            }

            // The source has to be a row in the allowlist. This is the check that keeps a
            // configuration screen from becoming a way to name an arbitrary endpoint.
            var source = await _repository.DataSourceAsync(field.Binding.SourceId, ct).ConfigureAwait(false);
            if (source is null)
            {
                return Result.Invalid("That data source is not one this product offers.", "binding");
            }

            if (string.IsNullOrWhiteSpace(field.Binding.ValueField) || string.IsNullOrWhiteSpace(field.Binding.LabelField))
            {
                return Result.Invalid("Pick which column supplies the value and which supplies the label.", "binding");
            }

            if (!string.IsNullOrWhiteSpace(field.Binding.StaticParamsJson) && !IsJsonObject(field.Binding.StaticParamsJson))
            {
                return Result.Invalid("The fixed parameters must be a JSON object.", "binding");
            }

            field.Binding.CacheSeconds = Math.Clamp(field.Binding.CacheSeconds, 0, 86_400);
            field.LookupKey = null;
            field.Options.Clear();
            return Result.Success();
        }

        if (string.Equals(field.DataSourceType, "Lookup", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(field.LookupKey) || !LookupKeys.Contains(field.LookupKey))
            {
                return Result.Invalid(
                    "Choose one of the built-in lists: department, designation, employee or leave type.", "lookupKey");
            }

            field.LookupKey = LookupKeys.First(k => string.Equals(k, field.LookupKey, StringComparison.OrdinalIgnoreCase));
            field.Options.Clear();
            field.Binding = null;
            return Result.Success();
        }

        if (string.Equals(field.DataSourceType, "Static", StringComparison.OrdinalIgnoreCase))
        {
            field.LookupKey = null;
            field.Binding = null;
            field.Options = field.Options
                .Where(o => !string.IsNullOrWhiteSpace(o.OptionValue))
                .Select((o, i) => new CustomFieldOption
                {
                    OptionValue = o.OptionValue.Trim(),
                    OptionLabel = string.IsNullOrWhiteSpace(o.OptionLabel) ? o.OptionValue.Trim() : o.OptionLabel.Trim(),
                    ParentValue = string.IsNullOrWhiteSpace(field.ParentFieldKey) || string.IsNullOrWhiteSpace(o.ParentValue)
                        ? null
                        : o.ParentValue.Trim(),
                    SeqNo = (i + 1) * 10
                })
                .ToList();

            return field.Options.Count == 0
                ? Result.Invalid("A dropdown needs at least one option.", "options")
                : Result.Success();
        }

        return Result.Invalid("Choose where this list gets its options from.", "dataSourceType");
    }

    /// <summary>
    /// Turns a posted string into the canonical text stored for that control type, and
    /// records a field error when it cannot. Dates are normalised to ISO so sorting the
    /// column and reading it back both behave.
    /// </summary>
    private static string? Coerce(CustomField field, string value, Validator errors)
    {
        if (field.MaxLength is > 0 && value.Length > field.MaxLength)
        {
            errors.Require(false, $"{field.Label} may be at most {field.MaxLength} characters.", field.FieldKey);
            return value;
        }

        if (!string.IsNullOrWhiteSpace(field.RegexPattern))
        {
            try
            {
                // A tenant-authored pattern is data, so a bad one must not take the save
                // down with it â€” an unusable pattern is reported as a field error.
                if (!Regex.IsMatch(value, field.RegexPattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                {
                    errors.Require(false, $"{field.Label} is not in the expected format.", field.FieldKey);
                }
            }
            catch (ArgumentException)
            {
                errors.Require(false, $"The validation pattern configured for {field.Label} is not usable.", field.FieldKey);
            }
            catch (RegexMatchTimeoutException)
            {
                errors.Require(false, $"The validation pattern configured for {field.Label} took too long to run.", field.FieldKey);
            }
        }

        switch (field.ControlType)
        {
            case "number":
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    errors.Require(false, $"{field.Label} must be a whole number.", field.FieldKey);
                    return value;
                }
                CheckRange(field, whole, errors);
                return whole.ToString(CultureInfo.InvariantCulture);

            case "decimal":
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    errors.Require(false, $"{field.Label} must be a number.", field.FieldKey);
                    return value;
                }
                CheckRange(field, number, errors);
                return number.ToString(CultureInfo.InvariantCulture);

            case "date":
                if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    errors.Require(false, $"{field.Label} must be a date.", field.FieldKey);
                    return value;
                }
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            case "datetime":
                if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
                {
                    errors.Require(false, $"{field.Label} must be a date and time.", field.FieldKey);
                    return value;
                }
                return moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            case "checkbox":
                return value is "1" or "true" or "True" or "on" ? "1" : "0";

            case "dropdown":
            case "radio":
                // Only a static list can be checked here. A Lookup-backed field names a row
                // in another table, and re-reading that table on every save would cost more
                // than it protects â€” the foreign screen owns that value's validity.
                if (string.Equals(field.DataSourceType, "Static", StringComparison.OrdinalIgnoreCase) &&
                    field.Options.Count > 0 &&
                    !field.Options.Any(o => string.Equals(o.OptionValue, value, StringComparison.Ordinal)))
                {
                    errors.Require(false, $"'{value}' is not one of the choices for {field.Label}.", field.FieldKey);
                }
                return value;

            default:
                return value;
        }
    }

    private static void CheckRange(CustomField field, decimal value, Validator errors)
    {
        if (decimal.TryParse(field.RangeMin, NumberStyles.Number, CultureInfo.InvariantCulture, out var min) && value < min)
        {
            errors.Require(false, $"{field.Label} may not be less than {field.RangeMin}.", field.FieldKey);
        }

        if (decimal.TryParse(field.RangeMax, NumberStyles.Number, CultureInfo.InvariantCulture, out var max) && value > max)
        {
            errors.Require(false, $"{field.Label} may not be more than {field.RangeMax}.", field.FieldKey);
        }
    }

    /// <summary>"Blood group" becomes "bloodGroup" â€” the shape a form key has to have.</summary>
    private static string KeyFrom(string label)
    {
        var words = label.Split(new[] { ' ', '-', '_', '/', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
                         .Where(w => w.Length > 0)
                         .ToList();

        if (words.Count == 0) return "field";

        var head = words[0].ToLowerInvariant();
        var tail = words.Skip(1).Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        var key = head + string.Concat(tail);

        // A label beginning with a digit would produce a key no JavaScript object should
        // carry as an identifier, so it gets a prefix rather than a rejection.
        if (!char.IsLetter(key[0])) key = "f" + char.ToUpperInvariant(key[0]) + key[1..];

        return key.Length > 80 ? key[..80] : key;
    }

    // -----------------------------------------------------------------
    // Computed fields
    // -----------------------------------------------------------------

    /// <summary>
    /// Checks a formula before it is stored, and caches the keys it reads.
    ///
    /// A formula is rejected here rather than at evaluation time for two reasons: a broken
    /// one stored today fails on somebody else's record tomorrow, and a cycle would make
    /// the evaluation order meaningless. The cached reference list is derived from the text,
    /// never taken from the caller â€” it is the parser's answer, not a claim.
    /// </summary>
    private async Task<Result> ValidateFormulaAsync(CustomField field, CancellationToken ct)
    {
        field.ValueMode = ValueModes.Contains(field.ValueMode) ? field.ValueMode : "Manual";
        field.RecalcMode = RecalcModes.Contains(field.RecalcMode) ? field.RecalcMode : "Always";

        if (!string.Equals(field.ValueMode, "Computed", StringComparison.OrdinalIgnoreCase))
        {
            // Switching a field back to Manual clears the formula rather than leaving a
            // stale one that nothing runs but the editor would still show.
            field.FormulaText = null;
            field.FormulaRefsCsv = null;
            field.RoundTo = null;
            field.RecalcMode = "Always";
            return Result.Success();
        }

        var control = Controls.First(c => string.Equals(c.ControlType, field.ControlType, StringComparison.OrdinalIgnoreCase));
        if (!control.IsNumeric)
        {
            return Result.Invalid("Only a number or decimal field can be calculated.", "valueMode");
        }

        if (string.IsNullOrWhiteSpace(field.FormulaText))
        {
            return Result.Invalid("A calculated field needs a formula.", "formulaText");
        }

        field.FormulaText = field.FormulaText.Trim();

        if (field.RoundTo is { } scale && (scale < 0 || scale > 6))
        {
            return Result.Invalid("Rounding must be between 0 and 6 decimal places.", "roundTo");
        }

        var parsed = FormulaEngine.Validate(field.FormulaText);
        if (!parsed.Ok && parsed.Error is not null)
        {
            return Result.Invalid(parsed.Error, "formulaText");
        }

        var siblings = await _repository.ListAsync(field.ScreenKey, ct).ConfigureAwait(false);
        var byKey = siblings.ToDictionary(f => f.FieldKey, f => f, StringComparer.OrdinalIgnoreCase);

        var screen = ScreenCatalog.Screens.FirstOrDefault(
            s => string.Equals(s.Key, field.ScreenKey, StringComparison.OrdinalIgnoreCase));

        var compiled = new HashSet<string>(
            screen?.Fields.Select(f => f.Key) ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var reference in parsed.Refs)
        {
            if (string.Equals(reference, field.FieldKey, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Invalid("A formula cannot read the field it fills in.", "formulaText");
            }

            // A formula may read any field on the same screen, compiled or custom. It may
            // not read one that does not exist: that would silently count as zero and the
            // author would never learn their key was a typo.
            if (!byKey.ContainsKey(reference) && !compiled.Contains(reference))
            {
                return Result.Invalid($"There is no field called '{reference}' on this screen.", "formulaText");
            }
        }

        if (FormulaEngine.HasCycle(field.FieldKey, field.FormulaText,
                key => byKey.TryGetValue(key, out var other) && other.FieldId != field.FieldId ? other.FormulaText : null))
        {
            return Result.Invalid("These formulas depend on each other in a loop.", "formulaText");
        }

        field.FormulaRefsCsv = parsed.Refs.Count == 0 ? null : string.Join(",", parsed.Refs);
        return Result.Success();
    }

    public async Task<Result<FormulaTestResult>> TestFormulaAsync(FormulaTestRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FormulaText))
        {
            return Result<FormulaTestResult>.Success(new FormulaTestResult
            {
                IsValid = false,
                Error = "This field has no formula."
            });
        }

        var values = new Dictionary<string, string?>(request.SampleValues, StringComparer.OrdinalIgnoreCase);
        var evaluated = FormulaEngine.Evaluate(request.FormulaText, values, request.RoundTo);

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(request.ScreenKey))
        {
            foreach (var field in await _repository.ListAsync(request.ScreenKey.Trim(), ct).ConfigureAwait(false))
            {
                labels[field.FieldKey] = field.Label;
            }
        }

        // A failure here is the author's answer, not a fault: the editor shows the message
        // beside the formula box, so it comes back as a successful call carrying IsValid.
        return Result<FormulaTestResult>.Success(new FormulaTestResult
        {
            IsValid = evaluated.Ok,
            Error = evaluated.Error,
            Value = evaluated.Ok ? evaluated.Value : null,
            Refs = evaluated.Refs.ToList(),
            Missing = evaluated.Missing.ToList(),
            Readable = Readable(request.FormulaText, labels)
        });
    }

    /// <summary>The formula with each key replaced by its caption, so it can be read aloud.</summary>
    private static string Readable(string formula, IReadOnlyDictionary<string, string> labels)
        => Regex.Replace(formula, @"\{\s*([A-Za-z_][A-Za-z0-9_]{0,79})\s*\}", m =>
            labels.TryGetValue(m.Groups[1].Value, out var label) ? label : m.Groups[1].Value);

    // -----------------------------------------------------------------
    // Bound dropdowns
    // -----------------------------------------------------------------

    public Task<IReadOnlyList<DataSource>> DataSourcesAsync(CancellationToken ct = default)
        => _repository.DataSourcesAsync(ct);

    public async Task<Result<FieldOptionsResult>> OptionsAsync(
        int fieldId, string? search, string? parentValue, CancellationToken ct = default)
    {
        var field = await _repository.GetAsync(fieldId, ct).ConfigureAwait(false);
        if (field is null) return Result<FieldOptionsResult>.NotFound("Field not found.");

        var noParentYet = string.IsNullOrWhiteSpace(parentValue);
        var cascades = !string.IsNullOrWhiteSpace(field.ParentFieldKey)
                    || !string.IsNullOrWhiteSpace(field.Binding?.ParentFieldKey);

        // A cascading list with nothing chosen upstream has no options yet. Answering with
        // every row would let somebody pick a district that belongs to another state.
        if (cascades && noParentYet) return Result<FieldOptionsResult>.Success(new FieldOptionsResult());

        if (string.Equals(field.DataSourceType, "Static", StringComparison.OrdinalIgnoreCase))
        {
            var options = field.Options
                .Where(o => !cascades || string.IsNullOrWhiteSpace(o.ParentValue) || o.ParentValue == parentValue)
                .Select(o => new FieldOption { Value = o.OptionValue, Label = o.OptionLabel })
                .ToList();

            return Result<FieldOptionsResult>.Success(new FieldOptionsResult { Options = options });
        }

        if (string.Equals(field.DataSourceType, "Lookup", StringComparison.OrdinalIgnoreCase))
        {
            var rows = await _repository.LookupAsync(field.LookupKey ?? string.Empty, ct).ConfigureAwait(false);
            var options = rows
                .Where(r => string.IsNullOrWhiteSpace(search) || r.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(r => new FieldOption { Value = r.Id.ToString(CultureInfo.InvariantCulture), Label = r.Label })
                .ToList();

            return Result<FieldOptionsResult>.Success(new FieldOptionsResult { Options = options });
        }

        if (!string.Equals(field.DataSourceType, "Dynamic", StringComparison.OrdinalIgnoreCase) || field.Binding is null)
        {
            return Result<FieldOptionsResult>.Success(new FieldOptionsResult());
        }

        var binding = field.Binding;

        if (string.Equals(binding.SourceType, "NamedQuery", StringComparison.OrdinalIgnoreCase))
        {
            var parameters = ParamsFrom(binding, search, parentValue);
            var run = await _namedQueries.RunAsync(binding.SourceKey ?? string.Empty, parameters, ct).ConfigureAwait(false);

            if (!run.Ok) return Result<FieldOptionsResult>.Success(new FieldOptionsResult());

            var options = run.Rows
                .Select(r => new FieldOption
                {
                    Value = Text(r, binding.ValueField),
                    Label = string.IsNullOrWhiteSpace(binding.LabelTemplate)
                        ? Text(r, binding.LabelField)
                        : Fill(binding.LabelTemplate, r)
                })
                .Where(o => o.Value.Length > 0)
                .ToList();

            return Result<FieldOptionsResult>.Success(new FieldOptionsResult { Options = options });
        }

        if (string.Equals(binding.SourceType, "Lookup", StringComparison.OrdinalIgnoreCase))
        {
            var rows = await _repository.LookupAsync(binding.SourceKey ?? string.Empty, ct).ConfigureAwait(false);
            var options = rows
                .Where(r => string.IsNullOrWhiteSpace(search) || r.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(r => new FieldOption { Value = r.Id.ToString(CultureInfo.InvariantCulture), Label = r.Label })
                .ToList();

            return Result<FieldOptionsResult>.Success(new FieldOptionsResult { Options = options });
        }

        // An Api source is resolved by the browser. This server calling itself would drop
        // the caller's identity, so the answer hands back the REGISTERED path â€” never one
        // the caller supplied â€” and the browser fetches it with its own token.
        return Result<FieldOptionsResult>.Success(new FieldOptionsResult
        {
            ResolveOnClient = true,
            RelativeUrl = binding.RelativeUrl,
            ResultPath = binding.ResultPath,
            ValueField = binding.ValueField,
            LabelField = binding.LabelField,
            LabelTemplate = binding.LabelTemplate,
            StaticParams = binding.StaticParamsJson,
            SearchParamName = binding.SearchParamName,
            ParentFieldKey = binding.ParentFieldKey,
            ParentParamName = binding.ParentParamName
        });
    }

    public async Task<Result<SourceProbeResult>> ProbeAsync(SourceProbeRequest request, CancellationToken ct = default)
    {
        var source = await _repository.DataSourceAsync(request.SourceId, ct).ConfigureAwait(false);
        if (source is null) return Result<SourceProbeResult>.NotFound("That data source is not one this product offers.");

        if (source.RequiresParent && string.IsNullOrWhiteSpace(request.ParentValue))
        {
            return Result<SourceProbeResult>.Success(new SourceProbeResult
            {
                RequiresParent = true,
                Error = "This source needs a sample parent value before it returns anything."
            });
        }

        if (string.Equals(source.SourceType, "Lookup", StringComparison.OrdinalIgnoreCase))
        {
            var rows = await _repository.LookupAsync(source.SourceKey ?? string.Empty, ct).ConfigureAwait(false);

            return Result<SourceProbeResult>.Success(new SourceProbeResult
            {
                Columns = new List<string> { "id", "label" },
                Rows = rows.Take(25)
                           .Select(r => new Dictionary<string, object?> { ["id"] = r.Id, ["label"] = r.Label })
                           .ToList(),
                SuggestedValueField = "id",
                SuggestedLabelField = "label"
            });
        }

        if (string.Equals(source.SourceType, "NamedQuery", StringComparison.OrdinalIgnoreCase))
        {
            var run = await _namedQueries
                .RunAsync(source.SourceKey ?? string.Empty, ParamsFrom(null, request.Search, request.ParentValue), ct)
                .ConfigureAwait(false);

            if (!run.Ok)
            {
                return Result<SourceProbeResult>.Success(new SourceProbeResult { Error = run.Error ?? "The query could not be run." });
            }

            return Result<SourceProbeResult>.Success(new SourceProbeResult
            {
                Columns = run.Columns.ToList(),
                Rows = run.Rows.Take(25).Select(r => new Dictionary<string, object?>(r)).ToList(),
                SuggestedValueField = source.DefaultValueField ?? run.Columns.FirstOrDefault(),
                SuggestedLabelField = source.DefaultLabelField ?? run.Columns.Skip(1).FirstOrDefault()
            });
        }

        // Same reasoning as OptionsAsync: an Api source is the browser's to call.
        return Result<SourceProbeResult>.Success(new SourceProbeResult
        {
            ProbeOnClient = true,
            RelativeUrl = source.RelativeUrl,
            ResultPath = request.ResultPath ?? source.DefaultResultPath,
            SuggestedValueField = source.DefaultValueField,
            SuggestedLabelField = source.DefaultLabelField
        });
    }

    /// <summary>
    /// The parameters a bound source is called with: the fixed ones the binding declares,
    /// plus the search term and the parent value under the names it configured. Nothing
    /// else is passed through, so a caller cannot smuggle a parameter into the query.
    /// </summary>
    private static Dictionary<string, object?> ParamsFrom(CustomFieldBinding? binding, string? search, string? parentValue)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(binding?.StaticParamsJson))
        {
            try
            {
                var fixedParams = JsonSerializer.Deserialize<Dictionary<string, object?>>(binding.StaticParamsJson);
                if (fixedParams is not null)
                {
                    foreach (var (key, value) in fixedParams) parameters[key] = value;
                }
            }
            catch (JsonException)
            {
                // A half-typed parameter blob must not stop the list from loading. The
                // editor refuses to save one, so this only covers a row written earlier.
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters[string.IsNullOrWhiteSpace(binding?.SearchParamName) ? "search" : binding!.SearchParamName!] = search;
        }

        if (!string.IsNullOrWhiteSpace(parentValue))
        {
            parameters[string.IsNullOrWhiteSpace(binding?.ParentParamName) ? "parentValue" : binding!.ParentParamName!] = parentValue;
        }

        return parameters;
    }

    private static string Text(IDictionary<string, object?> row, string column)
        => row.TryGetValue(column, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;

    /// <summary>"{deptName} - {deptCode}" against one row.</summary>
    private static string Fill(string template, IDictionary<string, object?> row)
        => Regex.Replace(template, @"\{(\w+)\}", m => Text(row, m.Groups[1].Value));

    private static bool IsJsonObject(string text)
    {
        try
        {
            using var parsed = JsonDocument.Parse(text);
            return parsed.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // -----------------------------------------------------------------
    // Audit and archive
    // -----------------------------------------------------------------

    public Task<PagedResult<CustomFieldAudit>> AuditAsync(PageRequest page, string? screenKey, CancellationToken ct = default)
        => _repository.AuditAsync(page, screenKey, ct);

    public Task<PagedResult<CustomValueArchiveRow>> ArchiveAsync(PageRequest page, int? fieldId, CancellationToken ct = default)
        => _repository.ArchiveAsync(page, fieldId, ct);

    /// <summary>
    /// Records one change to a field definition.
    ///
    /// Auditing must never be the reason a change fails: the write is already done by the
    /// time this runs, and throwing here would report a failure that did not happen. A
    /// failed audit is swallowed for that reason and for that reason only.
    /// </summary>
    private async Task AuditAsync(
        string? screenKey, int fieldId, string? fieldKey, string action,
        object? before, object? after, CancellationToken ct)
    {
        try
        {
            await _repository.AddAuditAsync(
                screenKey,
                fieldId,
                fieldKey,
                action,
                before is null ? null : JsonSerializer.Serialize(before, AuditShape),
                after is null ? null : JsonSerializer.Serialize(after, AuditShape),
                success: true,
                errorText: null,
                ct).ConfigureAwait(false);
        }
        catch
        {
            // Deliberately swallowed. See the summary above.
        }
    }
}
