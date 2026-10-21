using SQLite;

namespace InvoicingTool.Core.Models;

/// <summary>Devis ou facture.</summary>
public class CommercialDocument : EntityBase
{
    [Indexed]
    public DocumentType Type { get; set; }

    /// <summary>Numéro attribué à l'émission (ex. D-2026-0001, F-2026-0001). Vide pour un brouillon.</summary>
    [Indexed]
    public string? Number { get; set; }

    [Indexed]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    [Indexed]
    public Guid ClientId { get; set; }

    // Copie des coordonnées du client au moment de l'émission :
    // un document émis ne doit pas changer si la fiche client est modifiée ensuite.
    public string ClientName { get; set; } = string.Empty;
    public string? ClientAddress { get; set; }
    public string? ClientCompanyId { get; set; }

    public string? Title { get; set; }

    /// <summary>Adresse du chantier si différente de celle du client.</summary>
    public string? SiteAddress { get; set; }

    public DateTime IssueDate { get; set; } = DateTime.Today;

    /// <summary>Date de validité pour un devis, date d'échéance pour une facture.</summary>
    public DateTime? DueDate { get; set; }

    public DateTime? PaidDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Pour une facture créée depuis un devis : identifiant du devis d'origine.</summary>
    public Guid? SourceDocumentId { get; set; }

    /// <summary>Acompte déjà versé (TTC), déduit du net à payer.</summary>
    public decimal DepositAmount { get; set; }

    // Totaux dénormalisés pour l'affichage rapide des listes. Recalculés à chaque enregistrement.
    public decimal TotalExclTax { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalInclTax { get; set; }

    [Ignore]
    public bool IsEditable => Status == DocumentStatus.Draft;

    [Ignore]
    public decimal AmountDue => TotalInclTax - DepositAmount;

    [Ignore]
    public string DisplayNumber => Number ?? "Brouillon";
}
