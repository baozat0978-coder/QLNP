using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services.Accounts;

// Mỗi thao tác đọc/ghi kiểm tra lại tài khoản trong DB, kể cả trên circuit đang mở.
public sealed class AccountValidity(IDbContextFactory<AuthDbContext> factory)
{
    public const string ExpiryClaim = "leave:expires";
    public const int SessionMinutes = 120;
    public bool IsValid(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !long.TryParse(principal.FindFirstValue(ExpiryClaim), out var expires)
            || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return false;
        using var db = factory.CreateDbContext();
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = db.Users.AsNoTracking().SingleOrDefault(x => x.Id == id);
        if (user is null || !user.IsEnabled || user.LockoutEnd > DateTimeOffset.UtcNow
            || user.SecurityStamp != principal.FindFirstValue("AspNet.Identity.SecurityStamp"))
            return false;
        var role = principal.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(role, out var parsed) && Enum.IsDefined(parsed)
            && db.UserRoles.Any(ur => ur.UserId == id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role));
    }
}
