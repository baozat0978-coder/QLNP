using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Services.Business;

// Kiểm tra đăng nhập, vai trò và phạm vi lớp của người thao tác.
public sealed partial class LeaveRepository
{
    private void EnsureManagedClass(
    AppSession session,
    string className)
    {
        RequireRole(session, UserRole.TrungDoi);

        var classGroup = _data.Classes.FirstOrDefault(x =>
            x.TenLop.Equals(
                className,
                StringComparison.OrdinalIgnoreCase));

        if (classGroup is null)
            throw new ArgumentException("Không tìm thấy lớp.");

        if (!string.Equals(
            classGroup.ManagerUserId,
            session.UserId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new UserAccessException(
                "Bạn chỉ được thao tác với lớp được phân công.");
        }
    }

    private static void RequireClassStaff(AppSession session)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên được sửa/xóa lớp.");
    }

    private static void RequireStaff(AppSession session)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.Admin or UserRole.PhongQLHV))
            throw new UserAccessException("Chỉ Phòng QLHV hoặc Admin được quản lý học viên.");
    }

    private HashSet<string> AllowedClasses(AppSession session)
    {
        if (session.Role == UserRole.TrungDoi)
            return _data.Classes.Where(x => x.ManagerUserId == session.UserId).Select(x => x.TenLop).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (session.Role == UserRole.BoMon)
        {
            using var db = _store.Open();
            return db.ClassSubjects.Where(x => x.FacultyUserId == session.UserId).Select(x => x.ClassName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return _data.Classes.Select(x => x.TenLop).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void RequireLoggedIn(AppSession session)
    {
        session.EnsureValid();
    }

    private static void RequireRole(AppSession session, UserRole role)
    {
        RequireLoggedIn(session);
        if (session.Role != role)
            throw new UserAccessException("Tài khoản không có quyền thực hiện thao tác này.");
    }
}
