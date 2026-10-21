using SQLite;

namespace InvoicingTool.Core.Models;

public class Client : EntityBase
{
    /// <summary>Nom du particulier ou raison sociale de l'entreprise.</summary>
    [Indexed]
    public string Name { get; set; } = string.Empty;

    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }

    /// <summary>SIRET ou n° TVA intracommunautaire pour un client professionnel.</summary>
    public string? CompanyId { get; set; }

    public string? Notes { get; set; }

    [Ignore]
    public string FullAddress => string.Join(", ",
        new[] { Address, string.Join(" ", new[] { PostalCode, City }.Where(s => !string.IsNullOrWhiteSpace(s))) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
}
