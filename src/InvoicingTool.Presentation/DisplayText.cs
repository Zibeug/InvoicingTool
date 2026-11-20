using InvoicingTool.Core.Models;

namespace InvoicingTool.Presentation;

public static class DisplayText
{
    public static string Status(DocumentType type, DocumentStatus status) => status switch
    {
        DocumentStatus.Draft => "Brouillon",
        DocumentStatus.Issued => type == DocumentType.Quote ? "Envoyé" : "À payer",
        DocumentStatus.Accepted => "Accepté",
        DocumentStatus.Refused => "Refusé",
        DocumentStatus.Paid => "Payée",
        DocumentStatus.Cancelled => type == DocumentType.Quote ? "Annulé" : "Annulée",
        _ => status.ToString(),
    };

    /// <summary>Statut affiché dans les listes : une facture impayée échue apparaît « En retard (12 j) ».</summary>
    public static string StatusFor(CommercialDocument doc, DateTime today) =>
        IsOverdue(doc, today) ? $"En retard ({(today - doc.DueDate!.Value).Days} j)" : Status(doc.Type, doc.Status);

    public static bool IsOverdue(CommercialDocument doc, DateTime today) =>
        doc.Type == DocumentType.Invoice && doc.Status == DocumentStatus.Issued && doc.DueDate is { } due && today > due;

    public static string Type(DocumentType type) => type == DocumentType.Quote ? "Devis" : "Facture";
}
