using QuanLyNghiPhep.Data;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Services.Business;

// Môn học của lớp và khoa phụ trách.
public sealed partial class LeaveRepository
{
    public List<string> GetSubjectTags(AppSession session, string? className = null)
    {
        RequireLoggedIn(session);
        lock (_gate)
        {
            RefreshUnsafe();
            var query = GetRequests(session, className).Where(x => x.PhanLoaiNghi == LeaveCategories.TranhThu && x.TrangThai == LeaveStatuses.Approved);
            if (!string.IsNullOrWhiteSpace(className))
                query = query.Where(x => x.Lop.Equals(className.Trim(), StringComparison.OrdinalIgnoreCase));

            return query
                .SelectMany(x => x.DanhSachTagMon)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }
    }

    public List<FacultyOption> GetFacultyOptions(AppSession session)
    {
        RequireLoggedIn(session);

        if (session.Role is not (UserRole.TrungDoi or UserRole.Admin))
            throw new UserAccessException(
                "Bạn không có quyền xem danh sách khoa để phân công.");

        using var scope = _scopes.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AuthDbContext>();

        var facultyRole = UserRole.BoMon.ToString();

        return (
            from user in db.Users.AsNoTracking()
            join userRole in db.UserRoles
                on user.Id equals userRole.UserId
            join role in db.Roles
                on userRole.RoleId equals role.Id
            where role.Name == facultyRole
                && user.IsEnabled
                && user.FacultyId != ""
                && user.FacultyName != ""
            orderby user.FacultyName
            select new FacultyOption(
                user.Id,
                user.FacultyId,
                user.FacultyName)
        ).ToList();
    }

    public List<ClassSubject> GetFacultySubjects(AppSession session)
    {
        RequireRole(session, UserRole.BoMon);

        using var db = _store.Open();

        return db.ClassSubjects
            .AsNoTracking()
            .Where(x => x.FacultyUserId == session.UserId)
            .OrderBy(x => x.ClassName)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public List<ClassSubject> GetClassSubjects(
        AppSession session,
        string className)
    {
        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            EnsureManagedClass(session, className);

            using var db = _store.Open();

            return db.ClassSubjects
                .Where(x => x.ClassName == className)
                .OrderBy(x => x.Name)
                .ToList();
        }
    }

    public void AddClassSubject(
    AppSession session,
    string className,
    string code,
    string name,
    string facultyUserId)
    {
        code = code.Trim().ToUpperInvariant();
        name = name.Trim();

        if (code.Length is < 1 or > 40)
            throw new ArgumentException(
                "Vui lòng nhập mã môn, tối đa 40 ký tự.");

        if (name.Length is < 1 or > 150)
            throw new ArgumentException(
                "Vui lòng nhập tên môn, tối đa 150 ký tự.");

        var faculty = GetFacultyOptions(session)
            .FirstOrDefault(x => x.UserId == facultyUserId)
            ?? throw new ArgumentException(
                "Vui lòng chọn khoa đã được cấp tài khoản.");

        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            EnsureManagedClass(session, className);

            using var db = _store.Open();

            var existing = db.ClassSubjects.FirstOrDefault(x =>
                x.ClassName == className
                && x.Code.ToUpper() == code);

            if (existing is not null)
            {
                if (!string.IsNullOrWhiteSpace(existing.FacultyUserId))
                    throw new ArgumentException(
                        "Mã môn này đã có và đã được liên kết với khoa.");

                // Bổ sung liên kết cho môn cũ chưa được gán khoa.
                existing.FacultyUserId = faculty.UserId;
                existing.Faculty = faculty.Name;
            }
            else
            {
                db.ClassSubjects.Add(new ClassSubject
                {
                    ClassName = className,
                    Code = code,
                    Name = name,
                    Faculty = faculty.Name,
                    FacultyUserId = faculty.UserId
                });
            }

            db.SaveChanges();
        }
    }

    public void UpdateClassSubject(AppSession session, int id, string code, string name, string facultyUserId)
    {
        code = code?.Trim().ToUpperInvariant() ?? ""; name = name?.Trim() ?? "";
        if (code.Length is < 1 or > 40) throw new ArgumentException("Mã môn tối đa 40 ký tự.");
        if (name.Length is < 1 or > 150) throw new ArgumentException("Tên môn tối đa 150 ký tự.");
        var faculty = GetFacultyOptions(session).FirstOrDefault(x => x.UserId == facultyUserId)
            ?? throw new ArgumentException("Vui lòng chọn khoa đã được cấp tài khoản.");
        lock (_gate)
        {
            RefreshUnsafe();
            using var db = _store.Open();
            var subject = db.ClassSubjects.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Không tìm thấy môn học.");
            EnsureManagedClass(session, subject.ClassName);
            if (db.ClassSubjects.Any(x => x.ClassName == subject.ClassName && x.Id != id && x.Code.ToUpper() == code))
                throw new ArgumentException("Mã môn đã tồn tại trong lớp.");
            subject.Code = code; subject.Name = name; subject.Faculty = faculty.Name; subject.FacultyUserId = faculty.UserId;
            db.SaveChanges();
            LogUnsafe(session, "Sửa môn học", $"Lớp {subject.ClassName}: {code} - {name}");
            SaveUnsafe();
        }
    }

    public void DeleteClassSubject(AppSession session, int id)
    {
        lock (_gate)
        {
            RefreshUnsafe();
            using var db = _store.Open();
            var subject = db.ClassSubjects.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Không tìm thấy môn học.");
            EnsureManagedClass(session, subject.ClassName);
            if (_data.Requests.Any(x => x.SubjectId == id))
                throw new BusinessRuleException("Môn học đã được dùng trong đơn nghỉ nên không thể xóa.");
            db.ClassSubjects.Remove(subject);
            db.SaveChanges();
            LogUnsafe(session, "Xóa môn học", $"Lớp {subject.ClassName}: {subject.Code} - {subject.Name}");
            SaveUnsafe();
        }
    }
}
