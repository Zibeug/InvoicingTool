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

    public static string Type(DocumentType type) => type == DocumentType.Quote ? "Devis" : "Facture";
}
