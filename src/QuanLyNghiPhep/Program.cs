using Microsoft.Data.Sqlite;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Components;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Endpoints;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services;
using QuanLyNghiPhep.Services.Accounts;
using QuanLyNghiPhep.Services.Backup;
using QuanLyNghiPhep.Services.Business;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "AppData");
Directory.CreateDirectory(dataDirectory);
BackupService.ApplyPendingRestore(dataDirectory);
var accountConnection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDirectory, "accounts.db"), ForeignKeys = true };
builder.Services.AddDbContextFactory<AuthDbContext>(o => o.UseSqlite(accountConnection.ToString()));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = ValidationRules.PasswordMinLength;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredUniqueChars = 4;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<AuthDbContext>().AddDefaultTokenProviders();
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, RecoverablePasswordHasher>();
builder.Services.AddSingleton<RecoverablePasswordReader>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "LeaveManagement.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(AccountValidity.SessionMinutes);
    options.SlidingExpiration = false;
    options.LoginPath = "/";
    options.Events.OnValidatePrincipal = async context =>
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<AccountValidity>().IsValid(context.Principal!))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys"))).SetApplicationName("QuanLyNghiPhepHV");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingProvider>();
builder.Services.AddSingleton<AccountValidity>();
builder.Services.AddScoped<AppSession>();
builder.Services.AddScoped<NotifyService>();
builder.Services.AddSingleton<LeaveRepository>();
builder.Services.AddSingleton<BusinessStore>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<DailyBackupService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
LeaveCategories.DefaultWeekendAddress = builder.Configuration["Leave:DefaultAddress"] ?? LeaveCategories.DefaultWeekendAddress;
LeaveCategories.PlatoonPrefix = builder.Configuration["Leave:PlatoonPrefix"] ?? LeaveCategories.PlatoonPrefix;
LeaveCategories.TranhThuMonthlyLimit = builder.Configuration.GetValue("Leave:TranhThuMonthlyLimit", LeaveCategories.TranhThuMonthlyLimit);
var app = builder.Build();
await AccountSetup.InitializeAsync(app.Services, dataDirectory);
if (args.Length > 0 && args[0] == "--account")
{
    await AccountSetup.RunCommandAsync(app.Services, args);
    return;
}
// Khoa và môn học được nhập bởi người dùng, không tự tạo dữ liệu mẫu.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapAccountEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
