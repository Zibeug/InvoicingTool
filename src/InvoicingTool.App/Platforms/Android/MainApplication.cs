using Android.App;
using Android.Runtime;

namespace InvoicingTool.App;

// Debug : autorise le HTTP non chiffré pour tester la synchronisation avec un serveur local (docker compose).
// En production (Release), seul HTTPS est autorisé.
#if DEBUG
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
