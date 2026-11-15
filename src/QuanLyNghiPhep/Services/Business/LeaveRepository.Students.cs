using QuanLyNghiPhep.Data;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Services.Business;

// Hồ sơ học viên: thêm, sửa, ngừng theo dõi, khôi phục, xoá.
public sealed partial class LeaveRepository
{
    private string NewStudentCode(string className, int number, IEnumerable<Student>? pending = null)
    {
        var prefix = className.ToUpperInvariant();
        if (prefix.Length > 20)
            prefix = prefix[..20];
        string code;
        do
        {
            code = $"{prefix}-{number++:000}";
        } while (_data.Students.Concat(pending ?? []).Any(x => x.MaHocVien.Equals(code, StringComparison.OrdinalIgnoreCase)));
        return code;
    }

    public List<Student> GetStudents(AppSession session, string? className = null, bool includeInactive = false)
    {
        RequireLoggedIn(session);
        if (includeInactive && session.Role is not (UserRole.Admin or UserRole.PhongQLHV))
            throw new UserAccessException("Không có quyền xem hồ sơ ngừng theo dõi.");
        lock (_gate)
        {
            RefreshUnsafe();
            var allowed = AllowedClasses(session);
            if (!string.IsNullOrWhiteSpace(className) && !allowed.Contains(className.Trim()))
                throw new UserAccessException("Lớp không thuộc phạm vi được phân công.");
            return _data.Students.Where(x => allowed.Contains(x.Lop) && (includeInactive || x.IsActive))
                .Where(x => string.IsNullOrWhiteSpace(className) || x.Lop.Equals(className.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Lop).ThenBy(x => x.STT).Select(x => x.Clone()).ToList();
        }
    }

    public Student AddStudent(AppSession session, Student input)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên mới có quyền thêm học viên.");
        lock (_gate)
        {
            RefreshUnsafe();
            var lop = input.Lop?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(lop))
                throw new ArgumentException("Vui lòng chọn lớp cho học viên.");
            if (!_data.Classes.Any(x => x.TenLop.Equals(lop, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Hãy tạo lớp trước khi thêm học viên.");
            lop = CanonicalClass(lop);
            int nextStt = _data.Students.Where(x => x.Lop == lop).Select(x => x.STT).DefaultIfEmpty(0).Max() + 1;
            var candidate = input.Clone();
            candidate.Lop = lop;
            if (string.IsNullOrWhiteSpace(candidate.MaHocVien))
                candidate.MaHocVien = NewStudentCode(lop, nextStt);
            var student = ValidateStudent(candidate);
            if (_data.Students.Any(x => x.MaHocVien.Equals(student.MaHocVien, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Mã học viên đã tồn tại.");
            student.STT = nextStt;
            _data.Students.Add(student);
            LogUnsafe(session, "Thêm học viên", $"{student.HoTen} ({student.MaHocVien}) - Lớp {student.Lop}");
            SaveUnsafe();
            return student.Clone();
        }
    }

    public void UpdateStudent(AppSession session, string originalCode, Student input)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.PhongQLHV or UserRole.Admin))
            throw new UserAccessException("Chỉ Phòng Quản lý học viên mới có quyền sửa thông tin học viên.");
        lock (_gate)
        {
            RefreshUnsafe();
            var student = _data.Students.FirstOrDefault(x => x.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Không tìm thấy học viên.");
            originalCode = student.MaHocVien;
            var candidate = input.Clone();
            candidate.MaHocVien = string.IsNullOrWhiteSpace(candidate.MaHocVien) ? originalCode : candidate.MaHocVien;
            candidate.Lop = CanonicalClass(candidate.Lop);
            var updated = ValidateStudent(candidate);
            if (updated.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase))
                updated.MaHocVien = originalCode;
            if (!originalCode.Equals(updated.MaHocVien, StringComparison.OrdinalIgnoreCase)
                && _data.Students.Any(x => x.MaHocVien.Equals(updated.MaHocVien, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Mã học viên mới đã tồn tại.");
            if (!originalCode.Equals(updated.MaHocVien, StringComparison.Ordinal)
                && _data.Requests.Any(x => x.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessRuleException("Không đổi mã học viên đã có đơn để giữ nguyên liên kết và hồ sơ đã duyệt.");
            using (var db = _store.Open())
                if (originalCode != updated.MaHocVien && BusinessStore.HasLegacyStudentHistory(db, originalCode))
                    throw new BusinessRuleException("Không đổi mã học viên đã đăng ký môn học.");
            if (!_data.Classes.Any(x => x.TenLop.Equals(updated.Lop, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Lớp đích không tồn tại.");
            if (student.Lop != updated.Lop && _data.Requests.Any(x => x.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase) && x.TrangThai is LeaveStatuses.WaitingTrungDoi or LeaveStatuses.WaitingPhong))
                throw new BusinessRuleException("Cần xử lý xong đơn đang chờ trước khi chuyển lớp học viên.");
            if (student.Lop != updated.Lop)
                student.STT = _data.Students.Where(x => x.Lop == updated.Lop).Select(x => x.STT).DefaultIfEmpty(0).Max() + 1;
            student.MaHocVien = updated.MaHocVien;
            student.HoTen = updated.HoTen;
            student.Lop = updated.Lop;
            student.TrungDoi = updated.TrungDoi;
            student.NgaySinh = updated.NgaySinh;
            student.SoDienThoai = updated.SoDienThoai;
            foreach (var request in _data.Requests.Where(x => x.MaHocVien.Equals(originalCode, StringComparison.OrdinalIgnoreCase) && x.TrangThai == LeaveStatuses.WaitingTrungDoi))
            {
                request.MaHocVien = student.MaHocVien;
                request.HoTen = student.HoTen;
                request.Lop = student.Lop;
                request.TrungDoi = student.TrungDoi;
            }
            LogUnsafe(session, "Sửa học viên", $"{student.HoTen} ({student.MaHocVien})");
            SaveUnsafe();
        }
    }

    // Returns true for an unused record deleted permanently; false when archived.
    public bool DeleteStudent(AppSession session, string studentCode)
    {
        RequireStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            var student = _data.Students.FirstOrDefault(x => x.MaHocVien.Equals(studentCode, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Không tìm thấy học viên.");
            using var db = _store.Open();
            bool historical = _data.Requests.Any(x => x.MaHocVien.Equals(studentCode, StringComparison.OrdinalIgnoreCase))
                || BusinessStore.HasLegacyStudentHistory(db, studentCode);
            if (historical)
            {
                student.IsActive = false;
                LogUnsafe(session, "Ngừng theo dõi học viên", student.MaHocVien);
            }
            else
            {
                _data.Students.Remove(student);
                LogUnsafe(session, "Xóa học viên chưa có hồ sơ", student.MaHocVien);
            }
            SaveUnsafe();
            return !historical;
        }
    }

    public void ReactivateStudent(AppSession session, string studentCode)
    {
        RequireStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            var student = _data.Students.SingleOrDefault(x => x.MaHocVien.Equals(studentCode, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Không tìm thấy học viên.");
            student.IsActive = true;
            LogUnsafe(session, "Khôi phục theo dõi", studentCode);
            SaveUnsafe();
        }
    }

    private static Student ValidateStudent(Student input)
    {
        var code = input.MaHocVien?.Trim() ?? "";
        var name = input.HoTen?.Trim() ?? "";
        var lop = input.Lop?.Trim() ?? "";
        var trungDoi = string.IsNullOrWhiteSpace(input.TrungDoi) ? $"Trung đội {lop}" : input.TrungDoi.Trim();
        var ngaySinh = ValidationRules.BirthDate(input.NgaySinh);
        var sdt = ValidationRules.Phone(input.SoDienThoai);
        if (code.Length is < 1 or > 30 || name.Length is < 1 or > 100
            || lop.Length is < 1 or > 60 || trungDoi.Length is < 1 or > 80)
            throw new ArgumentException("Mã, họ tên, lớp và trung đội phải được nhập đầy đủ, đúng độ dài.");
        return new Student
        {
            STT = input.STT > 0 ? input.STT : 1,
            MaHocVien = code,
            HoTen = name,
            Lop = lop,
            TrungDoi = trungDoi,
            NgaySinh = ngaySinh,
            SoDienThoai = sdt
        };
    }
}
