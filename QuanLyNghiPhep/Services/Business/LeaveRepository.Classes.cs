using QuanLyNghiPhep.Data;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Services.Business;

// Lớp: tạo, đổi tên, xoá, phân công trung đội và tài khoản quản lý.
public sealed partial class LeaveRepository
{
    public List<ClassGroup> GetClasses(AppSession session)
    {
        RequireLoggedIn(session);
        lock (_gate)
        {
            RefreshUnsafe();
            var names = _data.Classes.Select(c => c.TenLop)
                .Concat(_data.Students.Select(s => s.Lop))
                .Concat(_data.Requests.Select(r => r.Lop))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var authScope = _scopes.CreateScope();
            var namesById = authScope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().ToDictionary(x => x.Id, x => x.UserName ?? "");
            var result = new List<ClassGroup>();
            foreach (var name in names)
            {
                var existing = _data.Classes.FirstOrDefault(c => c.TenLop.Equals(name, StringComparison.OrdinalIgnoreCase));
                var manager = existing is not null && namesById.TryGetValue(existing.ManagerUserId, out var currentName) ? currentName : existing?.NguoiTao ?? "";
                result.Add(new ClassGroup
                {
                    TenLop = existing?.TenLop ?? name,
                    NguoiTao = manager,
                    ManagerUserId = existing?.ManagerUserId ?? "",
                    NgayTao = existing?.NgayTao ?? DateTime.Today
                });
            }

            if (session.Role == UserRole.TrungDoi)
            {
                result = result
                    .Where(c => c.ManagerUserId == session.UserId)
                    .ToList();
            }
            else if (session.Role == UserRole.BoMon)
            {
                var allowed = AllowedClasses(session);
                result = result.Where(c => allowed.Contains(c.TenLop)).ToList();
            }

            return result;
        }
    }

    // Phòng Quản lý học viên tạo danh sách lớp mới và phân công tài khoản Chủ nhiệm trung đội quản lý
    public ClassGroup CreateClassWithStudents(AppSession session, string className, IReadOnlyList<Student> students, string? managerUserName = null)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên mới có quyền tạo danh sách lớp mới.");
        var cleanClass = className?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(cleanClass) || cleanClass.Length > 60)
            throw new ArgumentException("Vui lòng nhập tên lớp hợp lệ (ví dụ: B9D55).");
        if (students is null || students.Count == 0 || students.Count > ValidationRules.MaxImportRows)
            throw new ArgumentException($"Danh sách lớp phải có từ 1 đến {ValidationRules.MaxImportRows} học viên.");

        var assignedManager = FindManager(string.IsNullOrWhiteSpace(managerUserName) ? "trungdoi" : managerUserName.Trim());

        lock (_gate)
        {
            RefreshUnsafe();
            if (_data.Classes.Any(c => c.TenLop.Equals(cleanClass, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Lớp {cleanClass} đã tồn tại trong hệ thống.");

            var validatedList = new List<Student>();
            int stt = 1;
            foreach (var item in students)
            {
                var hoTen = item.HoTen?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(hoTen))
                {
                    if (!string.IsNullOrWhiteSpace(item.MaHocVien) || !string.IsNullOrWhiteSpace(item.NgaySinh) || !string.IsNullOrWhiteSpace(item.SoDienThoai))
                        throw new ArgumentException($"Dòng {stt}: thiếu họ tên học viên.");
                    continue;
                }
                var lop = string.IsNullOrWhiteSpace(item.Lop) ? cleanClass : item.Lop.Trim();
                var ngaySinh = ValidationRules.BirthDate(item.NgaySinh);
                var sdt = ValidationRules.Phone(item.SoDienThoai);
                if (!lop.Equals(cleanClass, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Dòng {stt}: lớp của học viên khác lớp đang tạo.");
                var maHv = string.IsNullOrWhiteSpace(item.MaHocVien) ? NewStudentCode(cleanClass, stt, validatedList) : item.MaHocVien.Trim();
                if (maHv.Length > 30 || hoTen.Length > 100)
                    throw new ArgumentException($"Dòng {stt}: mã/họ tên vượt độ dài cho phép.");
                if (_data.Students.Concat(validatedList).Any(x => x.MaHocVien.Equals(maHv, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"Dòng {stt}: mã học viên bị trùng.");

                validatedList.Add(new Student
                {
                    STT = stt++,
                    MaHocVien = maHv,
                    HoTen = hoTen,
                    Lop = cleanClass,
                    NgaySinh = ngaySinh,
                    SoDienThoai = sdt,
                    TrungDoi = $"{LeaveCategories.PlatoonPrefix} {cleanClass}"
                });
            }

            if (validatedList.Count == 0)
                throw new ArgumentException("Vui lòng nhập họ tên cho ít nhất 1 học viên.");

            var group = new ClassGroup
            {
                TenLop = cleanClass,
                NguoiTao = assignedManager.Name,
                ManagerUserId = assignedManager.Id,
                NgayTao = DateTime.Now
            };
            _data.Classes.Add(group);
            _data.Students.AddRange(validatedList);
            LogUnsafe(session, "Tạo danh sách lớp mới", $"Lớp {cleanClass} ({validatedList.Count} học viên) - Giao tài khoản {assignedManager.Name} quản lý");
            SaveUnsafe();
            return new ClassGroup { TenLop = group.TenLop, NguoiTao = group.NguoiTao, ManagerUserId = group.ManagerUserId, NgayTao = group.NgayTao };
        }
    }

    public void UpdateClassManager(AppSession session, string className, string managerUserName)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên mới có quyền phân công tài khoản quản lý lớp.");
        var cleanClass = className?.Trim() ?? "";
        var cleanManager = FindManager(string.IsNullOrWhiteSpace(managerUserName) ? "trungdoi" : managerUserName.Trim());
        lock (_gate)
        {
            RefreshUnsafe();
            var existing = _data.Classes.FirstOrDefault(c => c.TenLop.Equals(cleanClass, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                throw new ArgumentException("Không tìm thấy lớp.");
            existing.ManagerUserId = cleanManager.Id;
            existing.NguoiTao = cleanManager.Name;
            LogUnsafe(session, "Phân công quản lý lớp", $"Lớp {cleanClass} -> Tài khoản {cleanManager.Name}");
            SaveUnsafe();
        }
    }

    public void RenameClass(AppSession session, string oldName, string newName)
    {
        RequireLoggedIn(session);
        if (session.Role != UserRole.Admin)
            throw new UserAccessException("Chỉ Admin được đổi tên lớp.");
        newName = newName?.Trim() ?? "";
        if (newName.Length is < 1 or > 60)
            throw new ArgumentException("Tên lớp mới phải từ 1 đến 60 ký tự.");
        lock (_gate)
        {
            RefreshUnsafe();
            oldName = CanonicalClass(oldName);
            if (_data.Classes.Any(c => c.TenLop.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Lớp {newName} đã tồn tại.");
            using (var db = _store.Open())
            {
                using var tx = db.Database.BeginTransaction();
                db.Database.ExecuteSqlRaw("PRAGMA defer_foreign_keys = ON");
                db.Database.ExecuteSqlRaw("UPDATE \"Classes\" SET \"TenLop\" = {0} WHERE \"TenLop\" = {1}", newName, oldName);
                db.Database.ExecuteSqlRaw("UPDATE \"Students\" SET \"Lop\" = {0} WHERE \"Lop\" = {1}", newName, oldName);
                db.Database.ExecuteSqlRaw("UPDATE \"Students\" SET \"TrungDoi\" = {0} WHERE \"TrungDoi\" = {1}", $"{LeaveCategories.PlatoonPrefix} {newName}", $"{LeaveCategories.PlatoonPrefix} {oldName}");
                db.Database.ExecuteSqlRaw("UPDATE \"Requests\" SET \"Lop\" = {0} WHERE \"Lop\" = {1}", newName, oldName);
                db.Database.ExecuteSqlRaw("UPDATE \"ClassSubjects\" SET \"ClassName\" = {0} WHERE \"ClassName\" = {1}", newName, oldName);
                tx.Commit();
            }
            RefreshUnsafe();
            LogUnsafe(session, "Đổi tên lớp", $"{oldName} -> {newName}");
            SaveUnsafe();
        }
    }

    public void DeleteClass(AppSession session, string className)
    {
        RequireClassStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            if (_data.Requests.Any(x => x.Lop == className))
                throw new BusinessRuleException("Lớp đã có đơn nghỉ phép nên không thể xóa (để giữ lịch sử).");
            using (var db = _store.Open())
            {
                if (_data.Students.Where(x => x.Lop == className).Any(x => BusinessStore.HasLegacyStudentHistory(db, x.MaHocVien)))
                    throw new BusinessRuleException("Lớp có học viên đã đăng ký môn học nên không thể xóa.");
                db.ClassSubjects.RemoveRange(db.ClassSubjects.Where(x => x.ClassName == className));
                db.SaveChanges();
            }
            var count = _data.Students.RemoveAll(x => x.Lop == className);
            _data.Classes.RemoveAll(x => x.TenLop == className);
            LogUnsafe(session, "Xóa lớp", $"Lớp {className} ({count} học viên)");
            SaveUnsafe();
        }
    }

    public void SetClassPlatoon(AppSession session, string className, string platoon)
    {
        RequireLoggedIn(session);
        if (session.Role != UserRole.Admin)
            throw new UserAccessException("Chỉ Admin được gán trung đội cho lớp.");
        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            using (var db = _store.Open())
                if (!db.Platoons.Any(x => x.Name == platoon)) throw new ArgumentException("Hãy chọn trung đội trong danh mục.");
            foreach (var st in _data.Students.Where(x => x.Lop == className)) st.TrungDoi = platoon;
            LogUnsafe(session, "Gán trung đội cho lớp", $"Lớp {className} -> {platoon}");
            SaveUnsafe();
        }
    }
}
