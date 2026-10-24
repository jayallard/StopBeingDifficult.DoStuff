using Sbd.DoStuff.Domain.DependencyInjection;
using Sbd.DoStuff.WebApp.Components;

// A restarted instance is launched while the old one is still shutting down; wait so the port is free.
Sbd.DoStuff.WebApp.Services.AppRestarter.WaitForPreviousInstance();

var builder = WebApplication.CreateBuilder(args);

// Without this, Kestrel waits indefinitely for open Blazor Server SignalR
// circuits to drain on shutdown, so a stale browser tab hangs the process exit.
builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDoStuffDomain(builder.Configuration);
builder.Services.AddSingleton<Sbd.DoStuff.WebApp.Services.AppRestarter>();
builder.Services.AddSingleton<Sbd.DoStuff.WebApp.Services.FavoriteLists>();
builder.Services.AddSingleton<Sbd.DoStuff.WebApp.Services.PendingCommands>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<Sbd.DoStuff.WebApp.Services.PendingCommands>());
builder.Services.AddSingleton<Sbd.DoStuff.WebApp.Services.IFileSystemBrowser,Sbd.DoStuff.WebApp.Services.FileSystemBrowser>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapGet("/icons/{**name}", (string name, Sbd.DoStuff.Domain.Icons.TaskIconCatalog icons) =>
{
    if (!icons.TryFindFile(name, out var path) && !icons.TryFindFile(icons.DefaultIcon, out path))
    {
        return Results.NotFound();
    }

    var contentType = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/svg+xml";
    return Results.File(path, contentType, lastModified: File.GetLastWriteTimeUtc(path));
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();