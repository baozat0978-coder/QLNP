using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QuanLyNghiPhep.Data;

namespace QuanLyNghiPhep.Services.Accounts;

// Keep Identity hashing for authentication; encrypt the admin-visible copy separately.
public sealed class RecoverablePasswordHasher(IDataProtectionProvider protection, IOptions<PasswordHasherOptions> options)
    : PasswordHasher<ApplicationUser>(options)
{
    public override string HashPassword(ApplicationUser user, string password)
    {
        var hash = base.HashPassword(user, password);
        user.ProtectedPassword = protection.CreateProtector("AdminAccountPassword.v1", user.Id).Protect(password);
        return hash;
    }
}
