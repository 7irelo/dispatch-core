using Microsoft.AspNetCore.Authentication;
using DispatchCore.Core.Interfaces;
using DispatchCore.Dashboard.Components;
using DispatchCore.Storage;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

var connectionString = builder.Configuration.GetConnectionString("Postgres")!;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<IJobRepository>(new PostgresJobRepository(connectionString));

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "dispatch-dashboard";
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Dev-only login endpoint
app.MapPost("/login", async (HttpContext ctx) =>
{
    var claims = new[] { new System.Security.Claims.Claim("name", "admin") };
    var identity = new System.Security.Claims.ClaimsIdentity(claims, "Cookies");
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);
    await ctx.SignInAsync("Cookies", principal);
    ctx.Response.Redirect("/");
});

app.MapGet("/login", () => Results.Content("""
    <html><body style="font-family:sans-serif;display:flex;justify-content:center;align-items:center;height:100vh">
    <form method="post" action="/login">
    <h2>Dispatch Dashboard Login</h2>
    <button type="submit" style="padding:10px 20px;font-size:16px">Login as Admin</button>
    </form></body></html>
    """, "text/html"));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
