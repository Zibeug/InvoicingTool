namespace InvoicingTool.Core.Models;

/// <summary>Informations de l'entreprise de l'artisan, imprimées sur les devis et factures.</summary>
public class CompanySettings : EntityBase
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    public CompanySettings() => Id = SingletonId;

    public string CompanyName { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Siret { get; set; }
    public string? VatNumber { get; set; }

    /// <summary>Assurance décennale (obligatoire pour les métiers du bâtiment).</summary>
    public string? InsuranceInfo { get; set; }

    public string? Iban { get; set; }

    /// <summary>Micro-entreprise en franchise de TVA (art. 293 B du CGI).</summary>
    public bool VatExempt { get; set; }

    public decimal DefaultVatRate { get; set; } = 10m;
    public int QuoteValidityDays { get; set; } = 30;
    public int PaymentTermDays { get; set; } = 30;

    public string QuotePrefix { get; set; } = "D";
    public string InvoicePrefix { get; set; } = "F";

    public string? PaymentTerms { get; set; } =
        "En cas de retard de paiement, des pénalités au taux de 3 fois le taux d'intérêt légal seront appliquées, " +
        "ainsi qu'une indemnité forfaitaire pour frais de recouvrement de 40 €. Pas d'escompte pour paiement anticipé.";
}
