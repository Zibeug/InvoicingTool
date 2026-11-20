using InvoicingTool.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvoicingTool.Api.Tests;

/// <summary>API démarrée en mémoire sur une base PostgreSQL de test créée pour l'occasion, puis supprimée.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _database = $"invoicing_test_{Guid.NewGuid():N}";

    public static string ServerConnection =>
        Environment.GetEnvironmentVariable("INVOICING_TEST_POSTGRES") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    protected virtual int AuthRequestsPerMinute => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"{ServerConnection};Database={_database}",
            ["Jwt:Secret"] = "cle-de-test-uniquement-0123456789-abcdefghij",
            ["RateLimit:AuthPerMinute"] = AuthRequestsPerMinute.ToString(),
        }));
    }

    public Task InitializeAsync()
    {
        _ = Server; // démarre l'API : la base est créée par les migrations
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SyncDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }
}
