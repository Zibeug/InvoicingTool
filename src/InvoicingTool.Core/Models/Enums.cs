namespace InvoicingTool.Core.Models;

public enum DocumentType
{
    Quote = 0,
    Invoice = 1,
}

public enum DocumentStatus
{
    /// <summary>Modifiable, pas encore numéroté.</summary>
    Draft = 0,
    /// <summary>Numéroté et envoyé / présenté au client. Plus modifiable.</summary>
    Issued = 1,
    /// <summary>Devis accepté par le client.</summary>
    Accepted = 2,
    /// <summary>Devis refusé par le client.</summary>
    Refused = 3,
    /// <summary>Facture réglée.</summary>
    Paid = 4,
    /// <summary>Document annulé (une facture émise ne se supprime pas, elle s'annule).</summary>
    Cancelled = 5,
}
