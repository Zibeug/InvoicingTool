namespace InvoicingTool.Presentation.Abstractions;

public interface IDialogService
{
    Task AlertAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message, string accept = "Oui", string cancel = "Non");

    /// <summary>Affiche une liste de choix ; retourne le choix ou null si annulé.</summary>
    Task<string?> ChooseAsync(string title, IReadOnlyList<string> options, string cancel = "Annuler");
}
