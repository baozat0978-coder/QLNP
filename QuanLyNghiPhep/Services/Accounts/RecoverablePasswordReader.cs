using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QuanLyNghiPhep.Data;

namespace QuanLyNghiPhep.Services.Accounts;

public sealed class RecoverablePasswordReader(IDataProtectionProvider protection)
{
    public string Read(ApplicationUser user)
    {
        if (string.IsNullOrEmpty(user.ProtectedPassword)) return "";
        try
        {
            var password = protection.CreateProtector("AdminAccountPassword.v1", user.Id).Unprotect(user.ProtectedPassword);
            return new PasswordHasher<ApplicationUser>().VerifyHashedPassword(user, user.PasswordHash ?? "", password)
                == PasswordVerificationResult.Failed ? "" : password;
        }
        catch (CryptographicException) { return ""; }
    }
}
