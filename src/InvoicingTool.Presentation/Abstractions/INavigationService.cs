namespace InvoicingTool.Presentation.Abstractions;

public interface INavigationService
{
    /// <summary>Navigue vers une route (syntaxe Shell : « Page », « ../Page », « .. »).</summary>
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);

    Task GoBackAsync();
}
