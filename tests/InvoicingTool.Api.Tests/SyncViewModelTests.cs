using InvoicingTool.Core.Models;
using InvoicingTool.Presentation.Abstractions;
using InvoicingTool.Presentation.ViewModels;
using InvoicingTool.Tests;

namespace InvoicingTool.Api.Tests;

public sealed class FakeDeviceInfo(string name = "iPhone de Léa") : IDeviceInfoService
{
    public string DeviceName => name;
}

public class SyncViewModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static SyncViewModel Screen(Device device, FakeDialogs dialogs, string name = "iPhone de Léa") =>
        new(device.Api, device.Engine, device.Db.SyncState, new FakeDeviceInfo(name), device.Db.Clock, dialogs);

    [Fact]
    public async Task Sync_is_off_until_the_user_creates_an_account()
    {
        await using var phone = new Device(factory);
        var vm = Screen(phone, new FakeDialogs());
        await vm.OnAppearingAsync();

        Assert.False(vm.IsEnabled);
        Assert.Equal("Synchronisation désactivée sur cet appareil.", vm.AccountText);
        Assert.Equal("iPhone de Léa", vm.DeviceName);
    }

    [Fact]
    public async Task Creating_an_account_enables_sync_and_sends_existing_data()
    {
        await using var phone = new Device(factory);
        await phone.Db.Clients.SaveAsync(new Client { Name = "Café du Marché" });
        var vm = Screen(phone, new FakeDialogs());
        await vm.OnAppearingAsync();

        vm.ServerUrl = Device.Server;
        vm.Email = $"user-{Guid.NewGuid():N}@example.ch";
        vm.Password = "motdepasse123";
        await vm.RegisterCommand.ExecuteAsync(null);

        Assert.True(vm.IsEnabled);
        Assert.Equal(string.Empty, vm.Password); // le mot de passe n'est pas conservé
        Assert.Equal("Dernière synchronisation : à l'instant", vm.LastSyncText);
        Assert.StartsWith("Numérotation de cet appareil : série principale", vm.SeriesText);
        Assert.Empty(await phone.Db.Clients.GetPendingChangesAsync()); // tout a été envoyé
    }

    [Fact]
    public async Task Second_device_logs_in_gets_the_data_and_its_own_series()
    {
        await using var phone = new Device(factory);
        await using var pc = new Device(factory);
        var email = $"user-{Guid.NewGuid():N}@example.ch";
        await phone.Db.Clients.SaveAsync(new Client { Name = "Studio Lumen" });
        await phone.Api.RegisterAsync(Device.Server, email, "motdepasse123", "Téléphone");
        await phone.Engine.SyncAsync();

        var vm = Screen(pc, new FakeDialogs(), "PC-BUREAU");
        vm.ServerUrl = Device.Server;
        vm.Email = email;
        vm.Password = "motdepasse123";
        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("Studio Lumen", Assert.Single(await pc.Db.Clients.GetAllAsync()).Name);
        Assert.Contains("série B (ex. F-2026-B-0001)", vm.SeriesText);
    }

    [Fact]
    public async Task Wrong_password_is_shown_and_sync_stays_off()
    {
        await using var phone = new Device(factory);
        var email = $"user-{Guid.NewGuid():N}@example.ch";
        await phone.Api.RegisterAsync(Device.Server, email, "motdepasse123", "Téléphone");
        await phone.Api.LogoutAsync();

        var dialogs = new FakeDialogs();
        var vm = Screen(phone, dialogs);
        vm.ServerUrl = Device.Server;
        vm.Email = email;
        vm.Password = "mauvais-mdp";
        await vm.LoginCommand.ExecuteAsync(null);

        Assert.False(vm.IsEnabled);
        Assert.Equal("Adresse e-mail ou mot de passe incorrect.", Assert.Single(dialogs.Alerts));
    }

    [Fact]
    public async Task Logout_turns_sync_off_but_keeps_local_data()
    {
        await using var phone = new Device(factory);
        await phone.Db.Clients.SaveAsync(new Client { Name = "Studio Lumen" });
        await phone.Api.RegisterAsync(Device.Server, $"user-{Guid.NewGuid():N}@example.ch", "motdepasse123", "Téléphone");
        var vm = Screen(phone, new FakeDialogs { ConfirmResult = true });
        await vm.OnAppearingAsync();

        await vm.LogoutCommand.ExecuteAsync(null);

        Assert.False(vm.IsEnabled);
        Assert.Single(await phone.Db.Clients.GetAllAsync());
        Assert.Null(await phone.Tokens.GetAsync(Core.Sync.SyncApiClient.RefreshTokenKey));
    }

    [Fact]
    public async Task Background_sync_does_nothing_when_disabled_and_reports_received_data()
    {
        await using var phone = new Device(factory);
        await using var pc = new Device(factory);
        var background = new BackgroundSync(pc.Engine, pc.Db.SyncState);
        await background.TrySyncAsync(); // désactivée : aucun appel, aucune erreur

        var email = $"user-{Guid.NewGuid():N}@example.ch";
        await phone.Api.RegisterAsync(Device.Server, email, "motdepasse123", "Téléphone");
        await pc.Api.LoginAsync(Device.Server, email, "motdepasse123", "PC");
        await phone.Db.Clients.SaveAsync(new Client { Name = "Nouveau client" });
        await phone.Engine.SyncAsync();

        var changed = false;
        background.DataChanged += (_, _) => changed = true;
        await background.TrySyncAsync();

        Assert.True(changed);
        Assert.Single(await pc.Db.Clients.GetAllAsync());
    }
}
