using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services.Accounts;

public static class AccountSetup
{
    public static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
    // 10 ký tự, đủ chữ hoa/chữ thường/số/ký tự đặc biệt, bỏ các ký tự dễ nhầm (0/O, 1/l/I).
    public static string NewPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnpqrstuvwxyz", digit = "23456789", symbol = "@#$%*!?+";
        const string all = upper + lower + digit + symbol;
        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digit), Pick(symbol) };
        while (chars.Count < 10) chars.Add(Pick(all));
        for (int i = chars.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    public static async Task InitializeAsync(IServiceProvider services, string dataDirectory)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.EnsureCreatedAsync();
        await EnsureProfileColumnsAsync(db);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Enum.GetNames<UserRole>())
            if (!await roles.RoleExistsAsync(role))
                Check(await roles.CreateAsync(new IdentityRole(role)));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // Chỉ khởi tạo khi database chưa có người dùng; không reset mật khẩu khi chạy lại.
        if (users.Users.Any())
        {
            if (!(await users.GetUsersInRoleAsync(nameof(UserRole.Admin))).Any())
            {
                var name = "admin";
                while (await users.FindByNameAsync(name) is not null)
                    name = "admin_" + Guid.NewGuid().ToString("N")[..6];
                var password = NewPassword();
                await using var adminTransaction = await db.Database.BeginTransactionAsync();
                var admin = new ApplicationUser { UserName = name, LockoutEnabled = true };
                Check(await users.CreateAsync(admin, password));
                Check(await users.AddToRoleAsync(admin, nameof(UserRole.Admin)));
                await File.WriteAllTextAsync(Path.Combine(dataDirectory, "admin-ban-dau.txt"), $"{name}\tAdmin\t{password}\nXóa tệp sau khi bàn giao và đổi mật khẩu.");
                await adminTransaction.CommitAsync();
            }
            return;
        }
        var path = Path.Combine(dataDirectory, "tai-khoan-ban-dau.txt");
        if (File.Exists(path))
            throw new InvalidOperationException("DB tài khoản trống nhưng còn tệp tài khoản ban đầu. Hãy kiểm tra bản sao lưu trước khi khởi tạo lại.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var lines = new List<string> { "Tài khoản khởi tạo riêng cho máy này. Không chia sẻ hoặc đưa tệp này lên Git.", "Sau khi bàn giao tài khoản, xóa tệp này khỏi máy chủ." };
        foreach (var (name, role) in new[] { ("admin", UserRole.Admin), ("trungdoi", UserRole.TrungDoi), ("phongqlhv", UserRole.PhongQLHV), ("bomon", UserRole.BoMon) })
        {
            var password = NewPassword();
            var user = new ApplicationUser { UserName = name, LockoutEnabled = true };
            Check(await users.CreateAsync(user, password));
            Check(await users.AddToRoleAsync(user, role.ToString()));
            lines.Add($"{name}\t{role}\t{password}");
        }
        try
        {
            await File.WriteAllLinesAsync(path, lines);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await transaction.CommitAsync();
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    private static async Task EnsureProfileColumnsAsync(AuthDbContext db)
    {
        await db.Database.OpenConnectionAsync();

        try
        {
            var connection = db.Database.GetDbConnection();
            var existing = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"AspNetUsers\");";

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    existing.Add(reader.GetString(1));
                }
            }

            var columns = new[]
            {
                ("ProtectedPassword", "TEXT NOT NULL DEFAULT ''"),
                ("FullName", "TEXT NOT NULL DEFAULT ''"),
                ("DateOfBirth", "TEXT NULL"),
                ("MajorCode", "TEXT NOT NULL DEFAULT ''"),
                ("Gender", "TEXT NOT NULL DEFAULT ''"),
                ("FacultyName", "TEXT NOT NULL DEFAULT ''"),
                ("FacultyId", "TEXT NOT NULL DEFAULT ''")
            };

            foreach (var (name, definition) in columns)
            {
                if (existing.Contains(name))
                    continue;

                using var command = connection.CreateCommand();

                command.CommandText =
                    $"ALTER TABLE \"AspNetUsers\" " +
                    $"ADD COLUMN \"{name}\" {definition};";

                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // Công cụ dành cho người có quyền truy cập máy chủ, không phải endpoint công khai.
    public static async Task RunCommandAsync(IServiceProvider services, string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException("Cú pháp: --account create|reset-password|disable|enable username [TrungDoi|PhongQLHV|BoMon]");
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(args[2]);
        if (args[1] == "create")
        {
            if (args.Length != 4 || !Enum.TryParse<UserRole>(args[3], out var role) || !Enum.IsDefined(role))
                throw new ArgumentException("Vai trò không hợp lệ.");
            if (user is not null)
                throw new ArgumentException("Tài khoản đã tồn tại.");
            user = new ApplicationUser { UserName = args[2], LockoutEnabled = true };
            var password = NewPassword();
            Check(await users.CreateAsync(user, password));
            var result = await users.AddToRoleAsync(user, role.ToString());
            if (!result.Succeeded)
            {
                Check(await users.DeleteAsync(user));
                Check(result);
            }
            Console.WriteLine($"Mật khẩu mới (bàn giao riêng): {password}");
            return;
        }
        if (user is null)
            throw new ArgumentException("Không tìm thấy tài khoản.");
        switch (args[1])
        {
            case "reset-password":
                var password = NewPassword();
                Check(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password));
                Check(await users.SetLockoutEndDateAsync(user, null));
                Check(await users.ResetAccessFailedCountAsync(user));
                Console.WriteLine($"Mật khẩu mới (bàn giao riêng): {password}");
                break;
            case "disable":
            case "enable":
                user.IsEnabled = args[1] == "enable";
                Check(await users.UpdateAsync(user));
                Check(await users.UpdateSecurityStampAsync(user));
                Console.WriteLine("Đã cập nhật tài khoản.");
                break;
            default:
                throw new ArgumentException("Lệnh quản lý tài khoản không hợp lệ.");
        }
    }
}
