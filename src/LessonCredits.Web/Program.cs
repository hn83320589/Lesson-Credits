using LessonCredits.Core.Data;
using LessonCredits.Core.Services;
using LessonCredits.Core.Time;
using LessonCredits.Web.Auth;
using LessonCredits.Web.Components;
using LessonCredits.Web.Seed;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SQLite 相對路徑以工作目錄為準，固定到專案目錄避免從不同位置 dotnet run 產生多個資料庫
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "lessoncredits.db");
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TaipeiClock>();
builder.Services.AddScoped<CreditService>();
builder.Services.AddScoped<ScheduleService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<MakeupService>();
builder.Services.AddScoped<RefundService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddCookieAuth();

var app = builder.Build();

await DemoDataSeeder.InitializeAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
