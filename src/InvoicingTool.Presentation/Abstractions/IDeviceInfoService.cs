namespace InvoicingTool.Presentation.Abstractions;

public interface IDeviceInfoService
{
    /// <summary>Nom de l'appareil (« iPhone de Léa », « PC-BUREAU »), proposé à la connexion.</summary>
    string DeviceName { get; }
}
