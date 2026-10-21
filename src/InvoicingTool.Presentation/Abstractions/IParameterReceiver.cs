namespace InvoicingTool.Presentation.Abstractions;

/// <summary>Implémenté par les ViewModels qui reçoivent des paramètres de navigation.</summary>
public interface IParameterReceiver
{
    void ApplyParameters(IDictionary<string, object> parameters);
}
