namespace Quarry.Core.Documents;

/// <summary>
/// Structured metadata that travels with documents and chunks through the pipeline.
/// </summary>
/// <param name="Id">Stable, dataset-unique identifier (e.g. <c>hr-001</c>).</param>
/// <param name="Silo">One of: hr-policies, technical-docs, financial-reports, legal-contracts, product-catalog, release-notes-tickets.</param>
/// <param name="Department">HR, Engineering, Finance, Legal, Product.</param>
/// <param name="Office">Montreal, Paris, Casablanca.</param>
/// <param name="ConfidentialityLevel">Public, Internal, Restricted, Confidential.</param>
/// <param name="DocumentType">Policy, ADR, Runbook, Reference, Report, Contract, Specification, ReleaseNote, Ticket.</param>
/// <param name="FiscalYear">Year of last revision.</param>
/// <param name="Author">Free-text author name.</param>
/// <param name="LastModified">Date of the last revision.</param>
/// <param name="Title">Human-readable title.</param>
public sealed record DocumentMetadata(
    string Id,
    string Silo,
    string Department,
    string Office,
    string ConfidentialityLevel,
    string DocumentType,
    int FiscalYear,
    string Author,
    DateOnly LastModified,
    string Title)
{
    public Dictionary<string, string> ToDictionary() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = Id,
        ["silo"] = Silo,
        ["department"] = Department,
        ["office"] = Office,
        ["confidentialityLevel"] = ConfidentialityLevel,
        ["documentType"] = DocumentType,
        ["fiscalYear"] = FiscalYear.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["author"] = Author,
        ["lastModified"] = LastModified.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ["title"] = Title,
    };

    public static DocumentMetadata? FromDictionary(IReadOnlyDictionary<string, string>? dict)
    {
        if (dict is null)
        {
            return null;
        }

        if (!dict.TryGetValue("id", out var id))
        {
            return null;
        }

        dict.TryGetValue("silo", out var silo);
        dict.TryGetValue("department", out var department);
        dict.TryGetValue("office", out var office);
        dict.TryGetValue("confidentialityLevel", out var confidentiality);
        dict.TryGetValue("documentType", out var docType);
        int fiscalYear = 0;
        if (dict.TryGetValue("fiscalYear", out var fyStr))
        {
            int.TryParse(fyStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out fiscalYear);
        }
        dict.TryGetValue("author", out var author);
        DateOnly lastModified = DateOnly.MinValue;
        if (dict.TryGetValue("lastModified", out var lmStr))
        {
            DateOnly.TryParse(lmStr, System.Globalization.CultureInfo.InvariantCulture, out lastModified);
        }
        dict.TryGetValue("title", out var title);

        return new DocumentMetadata(
            id,
            silo ?? string.Empty,
            department ?? string.Empty,
            office ?? string.Empty,
            confidentiality ?? string.Empty,
            docType ?? string.Empty,
            fiscalYear,
            author ?? string.Empty,
            lastModified,
            title ?? string.Empty);
    }
}
