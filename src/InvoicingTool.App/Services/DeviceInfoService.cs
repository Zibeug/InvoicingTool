using InvoicingTool.Presentation.Abstractions;

namespace InvoicingTool.App.Services;

public sealed class DeviceInfoService : IDeviceInfoService
{
	public string DeviceName => string.IsNullOrWhiteSpace(DeviceInfo.Current.Name) ? "Mon appareil" : DeviceInfo.Current.Name;
}
