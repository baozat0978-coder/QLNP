using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Endpoints;

// Các endpoint form POST của tài khoản: đăng nhập, gia hạn phiên, đăng xuất, đổi mật khẩu.
// Mọi endpoint kiểm tra antiforgery; đăng nhập và đổi mật khẩu bị giới hạn tần suất.
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        // Tuần tự hóa cập nhật số lần sai trong một tiến trình, tránh mất lượt khi POST đồng thời.
        var loginGate = new SemaphoreSlim(1, 1);
        app.MapPost("/account/login", async (HttpContext http, IAntiforgery antiforgery,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Phiên biểu mẫu hết hạn. Hãy tải lại trang."); }
            var form = await http.Request.ReadFormAsync();
            string username = form["username"].ToString().Trim();
            string password = form["password"].ToString();

            if (username.Length is < 1 or > 256
                || password.Length is < 1 or > 256)
            {
                return Results.LocalRedirect("/?loginError=invalid");
            }
            await loginGate.WaitAsync(http.RequestAborted);
            try
            {
                var user = await users.FindByNameAsync(username);
                if (user is null || !user.IsEnabled)
                    return Results.LocalRedirect("/?loginError=invalid");
                var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
                if (!result.Succeeded)
                    return Results.LocalRedirect("/?loginError=invalid");

                // Lấy quyền được cấp trong database.
                var roles = await users.GetRolesAsync(user);

                if (roles.Count != 1
                    || !Enum.TryParse<UserRole>(roles[0], out var assignedRole)
                    || !Enum.IsDefined(assignedRole))
                {
                    return Results.LocalRedirect("/?loginError=invalid");
                }
                var expires = DateTimeOffset.UtcNow.AddMinutes(AccountValidity.SessionMinutes);
                await signIn.SignInWithClaimsAsync(user, new AuthenticationProperties { IsPersistent = false, ExpiresUtc = expires },
                    new[] { new Claim(AccountValidity.ExpiryClaim, expires.ToUnixTimeSeconds().ToString()) });
                return Results.LocalRedirect("/");
            }
            finally { loginGate.Release(); }
        }).RequireRateLimiting("login");
        app.MapPost("/account/renew", async (HttpContext http, IAntiforgery antiforgery,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AccountValidity validity) =>
        {
            try { await antiforgery.ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Phiên biểu mẫu hết hạn."); }
            if (!validity.IsValid(http.User)) return Results.LocalRedirect("/");
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.LocalRedirect("/");
            var expires = DateTimeOffset.UtcNow.AddMinutes(AccountValidity.SessionMinutes);
            await signIn.SignInWithClaimsAsync(user, new AuthenticationProperties { IsPersistent = false, ExpiresUtc = expires },
                new[] { new Claim(AccountValidity.ExpiryClaim, expires.ToUnixTimeSeconds().ToString()) });
            return Results.LocalRedirect("/");
        }).RequireAuthorization();
        app.MapPost("/account/logout", async (HttpContext http, IAntiforgery antiforgery,
            UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Phiên biểu mẫu hết hạn. Hãy tải lại trang."); }
            var user = await users.GetUserAsync(http.User);
            // Thu hồi cả các circuit và cookie cũ của cùng tài khoản.
            if (user is not null)
                AccountSetup.Check(await users.UpdateSecurityStampAsync(user));
            await signIn.SignOutAsync();
            return Results.LocalRedirect("/");
        }).RequireAuthorization();
        app.MapPost("/account/change-password", async (HttpContext http, IAntiforgery antiforgery, UserManager<ApplicationUser> users, AccountValidity validity) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException) { return Results.BadRequest("Phiên biểu mẫu hết hạn."); }
            if (!validity.IsValid(http.User))
                return Results.LocalRedirect("/");
            var form = await http.Request.ReadFormAsync();
            string current = form["current"].ToString(), next = form["next"].ToString(), confirm = form["confirm"].ToString();
            if (next.Length < ValidationRules.PasswordMinLength || next.Length > ValidationRules.PasswordMaxLength || current.Length > 256 || next != confirm || next == current)
                return Results.LocalRedirect("/password?error=validation");
            await loginGate.WaitAsync(http.RequestAborted);
            try
            {
                var user = await users.GetUserAsync(http.User);
                if (user is null || !validity.IsValid(http.User))
                    return Results.LocalRedirect("/");
                var result = await users.ChangePasswordAsync(user, current, next);
                if (!result.Succeeded)
                    return Results.LocalRedirect("/password?error=invalid");
                await http.SignOutAsync(IdentityConstants.ApplicationScheme);
                return Results.LocalRedirect("/?passwordChanged=1");
            }
            finally { loginGate.Release(); }
        }).RequireAuthorization().RequireRateLimiting("login");
    }
}
