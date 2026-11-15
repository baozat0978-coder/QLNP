using QuanLyNghiPhep.Data;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Services.Business;

// Nhật ký thao tác nghiệp vụ.
public sealed partial class LeaveRepository
{
    public List<AuditEntry> GetAudit(AppSession session)
    {
        RequireLoggedIn(session);
        if (session.Role != UserRole.Admin)
            throw new UserAccessException("Chỉ Admin được xem nhật ký hệ thống.");
        lock (_gate)
        {
            RefreshUnsafe();
            using var db = _store.Open();
            return db.Audit.AsNoTracking().OrderByDescending(x => x.At).ThenByDescending(x => x.Id).Take(300).ToList();
        }
    }

    public List<AuditView> GetAuditView(AppSession session)
    {
        var rows = GetAudit(session);
        using var scope = _scopes.CreateScope();
        var names = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().ToDictionary(x => x.Id, x => x.UserName ?? "");
        return rows.Select(x => new AuditView(x.At, names.TryGetValue(x.User, out var n) ? n : x.User, x.Action, x.Detail)).ToList();
    }

    private void LogUnsafe(AppSession session, string action, string detail)
    {
        _data.Audit.Add(new AuditEntry
        {
            At = DateTime.Now,
            User = session.UserId,
            Action = action,
            Detail = detail
        });
    }
}
