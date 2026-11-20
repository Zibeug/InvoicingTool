using SQLite;

namespace InvoicingTool.Core.Models;

/// <summary>
/// Rappel de paiement envoyé pour une facture impayée (1er rappel, 2e rappel, dernier rappel).
/// La facture émise n'est pas modifiée : chaque rappel est un document à part, avec son propre délai.
/// </summary>
public class PaymentReminder : EntityBase
{
    public const int MaxLevel = 3;

    [Indexed]
    public Guid DocumentId { get; set; }

    /// <summary>1 = rappel, 2 = 2e rappel, 3 = dernier rappel.</summary>
    public int Level { get; set; }

    public DateTime ReminderDate { get; set; }

    /// <summary>Nouveau délai de paiement accordé par ce rappel.</summary>
    public DateTime NewDueDate { get; set; }

    /// <summary>Frais de ce rappel (s'ajoutent à ceux des rappels précédents).</summary>
    public decimal Fee { get; set; }

    /// <summary>Montant total réclamé : facture + frais de tous les rappels jusqu'à celui-ci.</summary>
    public decimal TotalDue { get; set; }

    [Ignore]
    public string LevelName => LevelLabel(Level);

    public static string LevelLabel(int level) => level switch
    {
        1 => "1er rappel",
        2 => "2e rappel",
        _ => "Dernier rappel",
    };
}
