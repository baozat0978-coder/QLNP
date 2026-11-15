using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;
using QuanLyNghiPhep.Services.Excel;

namespace QuanLyNghiPhep.Services.Business;

// Đơn nghỉ: lập danh sách, nghỉ tranh thủ, sửa, xoá, tra cứu và xuất Excel.
public sealed partial class LeaveRepository
{
    public List<LeaveRequest> GetRequests(AppSession session, string? className = null)
    {
        RequireLoggedIn(session);
        if (session.Role == UserRole.BoMon)
            return GetFacultyRequests(session, className);
        lock (_gate)
        {
            RefreshUnsafe();
            var allowed = AllowedClasses(session);
            if (!string.IsNullOrWhiteSpace(className) && !allowed.Contains(className.Trim()))
                throw new UserAccessException("Lớp không thuộc phạm vi được phân công.");
            var query = _data.Requests.Where(x => allowed.Contains(x.Lop));
            if (!string.IsNullOrWhiteSpace(className))
                query = query.Where(x => x.Lop.Equals(className.Trim(), StringComparison.OrdinalIgnoreCase));

            // Với đơn quy trình 2 bước (nếu có), kiểm tra trạng thái; với đơn đã duyệt trực tiếp từ Trung đội (TrangThai == Approved) đẩy sang cả Phòng QLHV và Giáo viên bộ môn
            query = query.Where(x => session.Role switch
            {
                UserRole.PhongQLHV => x.TrangThai != LeaveStatuses.WaitingTrungDoi,
                UserRole.BoMon => x.TrangThai == LeaveStatuses.Approved,
                _ => true
            });

            var result = query
                .OrderByDescending(x => x.TuThoiDiem)
                .ThenByDescending(x => x.Id)
                .Select(x => x.Clone())
                .ToList();
            return result;
        }
    }

    // Duyệt danh sách nghỉ cuối tuần hoặc nghỉ Lễ/Tết (hàng loạt) -> Đẩy lên lưu trữ Phòng QLHV
    public List<LeaveRequest> CreateBatchLeaveList(
        AppSession session,
        string className,
        string phanLoaiNghi,
        string tagLoaiNghi,
        DateTime tuThoiDiem,
        DateTime denThoiDiem,
        IReadOnlyList<BatchLeaveDraftItem> items)
    {
        RequireRole(session, UserRole.TrungDoi);
        if (string.IsNullOrWhiteSpace(className))
            throw new ArgumentException("Vui lòng chọn lớp.");
        if (denThoiDiem <= tuThoiDiem)
            throw new ArgumentException("Thời gian kết thúc nghỉ phải sau thời gian bắt đầu.");
        if ((denThoiDiem - tuThoiDiem).TotalDays > 60 || (tagLoaiNghi?.Length ?? 0) > 300)
            throw new ArgumentException("Thời gian nghỉ không quá 60 ngày; tên đợt nghỉ tối đa 300 ký tự.");
        if (items is null || items.Count == 0)
            throw new ArgumentException("Danh sách nghỉ không có học viên nào.");

        string cleanCategory = phanLoaiNghi == LeaveCategories.LeTet ? LeaveCategories.LeTet : LeaveCategories.CuoiTuan;
        string cleanTag = cleanCategory == LeaveCategories.CuoiTuan
            ? LeaveCategories.TagCuoiTuan
            : (string.IsNullOrWhiteSpace(tagLoaiNghi) ? "Nghỉ Lễ/Tết" : tagLoaiNghi.Trim());

        lock (_gate)
        {
            RefreshUnsafe();
            className = CanonicalClass(className);
            EnsureManagedClass(session, className);
            var activeItems = items.Where(x => !string.IsNullOrWhiteSpace(x.HoTen)).ToList();
            if (activeItems.Count == 0 || activeItems.Select(x => x.MaHocVien).Distinct(StringComparer.OrdinalIgnoreCase).Count() != activeItems.Count
                || activeItems.Any(x => x.DiaChiNoiNghi.Length > 300 || !_data.Students.Any(s => s.MaHocVien == x.MaHocVien && s.Lop == className && s.IsActive)))
                throw new ArgumentException("Danh sách phải có học viên hợp lệ, không trùng mã, thuộc lớp đã chọn; địa chỉ tối đa 300 ký tự.");
            foreach (var item in activeItems)
            {
                EnsureNoOverlappingLeave(
                    item.MaHocVien,
                    tuThoiDiem,
                    denThoiDiem);
            }
            var created = new List<LeaveRequest>();
            var now = DateTime.Now;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.HoTen))
                    continue;
                var id = _data.NextId++;
                var addr = string.IsNullOrWhiteSpace(item.DiaChiNoiNghi)
                    ? LeaveCategories.DefaultWeekendAddress
                    : item.DiaChiNoiNghi.Trim();

                var req = new LeaveRequest
                {
                    Id = id,
                    MaDon = $"NP{now:yyyy}-{id:0000}",
                    MaHocVien = string.IsNullOrWhiteSpace(item.MaHocVien) ? $"{className}-{item.STT:000}" : item.MaHocVien,
                    HoTen = item.HoTen.Trim(),
                    Lop = className.Trim(),
                    TrungDoi = PlatoonOf(item.MaHocVien, className.Trim()),
                    TuThoiDiem = tuThoiDiem,
                    DenThoiDiem = denThoiDiem,
                    LyDo = cleanTag,
                    DiaChiNoiNghi = addr,
                    TrangThai = LeaveStatuses.WaitingPhong,
                    PhanLoaiNghi = cleanCategory,
                    TagLoaiNghi = cleanTag,
                    TagMonHoc = "",
                    NgayTao = now,
                    NguoiTao = session.UserName,
                    NguoiDuyetTrungDoi = session.UserName,
                    NgayDuyetTrungDoi = now,
                    NguoiDuyet = session.UserName,
                    NgayDuyet = now
                };
                _data.Requests.Add(req);
                created.Add(req.Clone());
            }

            LogUnsafe(session, $"Duyệt danh sách {cleanTag}", $"Lớp {className} ({created.Count} học viên) - Chờ Phòng QLHV duyệt");
            SaveUnsafe();
            return created;
        }
    }

    // Duyệt đơn nghỉ phép tranh thủ (có Tag 2 môn học để link sang Giáo viên bộ môn và đẩy lên Phòng QLHV)
    public LeaveRequest CreateTranhThuLeave(AppSession session, TranhThuLeaveInput input)
    {
        RequireRole(session, UserRole.TrungDoi);
        var ten = input.TenHocVien?.Trim() ?? "";
        var lop = input.Lop?.Trim() ?? "";
        var lyDo = input.LyDo?.Trim() ?? "";
        var diaChi = string.IsNullOrWhiteSpace(input.DiaChiNoiNghi) ? LeaveCategories.DefaultWeekendAddress : input.DiaChiNoiNghi.Trim();
        var monHoc = input.MonHoc?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(ten))
            throw new ArgumentException("Vui lòng nhập họ tên học viên xin nghỉ phép tranh thủ.");
        if (string.IsNullOrWhiteSpace(lop))
            throw new ArgumentException("Vui lòng chọn lớp.");
        if (input.DenThoiDiem <= input.TuThoiDiem)
            throw new ArgumentException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if (string.IsNullOrWhiteSpace(lyDo))
            throw new ArgumentException("Vui lòng nhập lý do xin nghỉ.");
        if (ten.Length > 100 || lyDo.Length > 500 || diaChi.Length > 300 || monHoc.Length > 300 || (input.DenThoiDiem - input.TuThoiDiem).TotalDays > 60)
            throw new ArgumentException("Dữ liệu vượt độ dài cho phép hoặc thời gian nghỉ quá 60 ngày.");

        lock (_gate)
        {
            RefreshUnsafe();
            lop = CanonicalClass(lop);
            EnsureManagedClass(session, lop);
            var matches = _data.Students.Where(s => s.IsActive && s.Lop.Equals(lop, StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(input.MaHocVien) ? s.MaHocVien.Equals(input.MaHocVien, StringComparison.OrdinalIgnoreCase) : s.HoTen.Equals(ten, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count != 1)
                throw new ArgumentException("Hãy chọn mã học viên hợp lệ; họ tên không có hoặc bị trùng trong lớp.");
            var matchedStudent = matches[0];
            EnsureNoOverlappingLeave(
               matchedStudent.MaHocVien,
               input.TuThoiDiem,
               input.DenThoiDiem);
            ClassSubject? selectedSubject = null;

            if (input.SubjectId.HasValue && input.SubjectId.Value > 0)
            {
                using (var db = _store.Open())
                {
                    EnsureManagedClass(session, lop);

                    selectedSubject = db.ClassSubjects
                        .AsNoTracking()
                        .SingleOrDefault(x =>
                            x.Id == input.SubjectId.Value
                            && x.ClassName == lop)
                        ?? throw new ArgumentException(
                            "Môn học không thuộc lớp đang chọn.");

                    if (string.IsNullOrWhiteSpace(selectedSubject.FacultyUserId))
                        throw new ArgumentException(
                            "Môn học chưa được liên kết với tài khoản khoa.");
                    if (!GetFacultyOptions(session).Any(x => x.UserId == selectedSubject.FacultyUserId))
                        throw new ArgumentException("Tài khoản khoa phụ trách đã ngừng hoạt động hoặc không còn hợp lệ.");

                    monHoc = selectedSubject.Name;
                }
            }
            else
            {
                monHoc = "";
            }

            string maHv = matchedStudent?.MaHocVien ?? (!string.IsNullOrWhiteSpace(input.MaHocVien) ? input.MaHocVien.Trim() : $"{lop}-{_data.NextId:000}");
            string hoTen = matchedStudent?.HoTen ?? ten;

            var now = DateTime.Now;
            var id = _data.NextId++;
            var request = new LeaveRequest
            {
                Id = id,
                MaDon = $"NP{now:yyyy}-{id:0000}",
                MaHocVien = maHv,
                HoTen = hoTen,
                Lop = lop,
                TrungDoi = matchedStudent.TrungDoi is { Length: > 0 } ? matchedStudent.TrungDoi : $"{LeaveCategories.PlatoonPrefix} {lop}",
                TuThoiDiem = input.TuThoiDiem,
                DenThoiDiem = input.DenThoiDiem,
                LyDo = lyDo,
                DiaChiNoiNghi = diaChi,
                TrangThai = LeaveStatuses.WaitingPhong,
                PhanLoaiNghi = LeaveCategories.TranhThu,
                TagLoaiNghi = LeaveCategories.TagTranhThu,
                TagMonHoc = monHoc,
                SubjectId = selectedSubject?.Id,
                NgayTao = now,
                NguoiTao = session.UserName,
                NguoiDuyetTrungDoi = session.UserName,
                NgayDuyetTrungDoi = now,
                NguoiDuyet = session.UserName,
                NgayDuyet = now
            };

            _data.Requests.Add(request);
            var logMon = string.IsNullOrWhiteSpace(request.TagMonHoc) ? "Không trùng lịch học" : request.TagMonHoc;
            LogUnsafe(session, "Duyệt nghỉ phép tranh thủ", $"{request.HoTen} ({request.Lop}) - Môn: {logMon}");
            SaveUnsafe();
            return request.Clone();
        }
    }

    public LeaveRequest Add(AppSession session, LeaveInput input)
    {
        RequireRole(session, UserRole.TrungDoi);
        lock (_gate)
        {
            RefreshUnsafe();
            var student = ValidateInput(input);
            EnsureManagedClass(session, student.Lop);
            EnsureNoOverlappingLeave(
                student.MaHocVien,
                input.TuThoiDiem,
                input.DenThoiDiem);
            var id = _data.NextId++;
            var request = new LeaveRequest
            {
                Id = id,
                MaDon = $"NP{DateTime.Today:yyyy}-{id:0000}",
                MaHocVien = student.MaHocVien,
                HoTen = student.HoTen,
                Lop = student.Lop,
                TrungDoi = input.TrungDoi.Trim(),
                TuThoiDiem = input.TuThoiDiem,
                DenThoiDiem = input.DenThoiDiem,
                LyDo = input.LyDo.Trim(),
                DiaChiNoiNghi = input.DiaChiNoiNghi.Trim(),
                PhanLoaiNghi = input.PhanLoaiNghi,
                TagLoaiNghi = string.IsNullOrWhiteSpace(input.TagLoaiNghi) ? LeaveCategories.TagTranhThu : input.TagLoaiNghi.Trim(),
                TagMonHoc = input.TagMonHoc?.Trim() ?? "",
                TrangThai = LeaveStatuses.WaitingTrungDoi,
                NgayTao = DateTime.Now,
                NguoiTao = session.UserName
            };
            _data.Requests.Add(request);
            LogUnsafe(session, "Tạo đơn", $"{request.HoTen} ({request.MaHocVien})");
            SaveUnsafe();
            return request.Clone();
        }
    }

    public LeaveRequest Update(AppSession session, int id, LeaveInput input)
    {
        RequireRole(session, UserRole.TrungDoi);
        lock (_gate)
        {
            RefreshUnsafe();
            var request = FindUnsafe(id);
            if (session.Role == UserRole.TrungDoi)
                EnsureManagedClass(session, request.Lop);
            RequirePending(request);
            var student = ValidateInput(input);
            EnsureManagedClass(session, student.Lop);
            EnsureNoOverlappingLeave(
                student.MaHocVien,
                input.TuThoiDiem,
                input.DenThoiDiem,
                id);
            request.MaHocVien = student.MaHocVien;
            request.HoTen = student.HoTen;
            request.Lop = student.Lop;
            request.TrungDoi = input.TrungDoi.Trim();
            request.TuThoiDiem = input.TuThoiDiem;
            request.DenThoiDiem = input.DenThoiDiem;
            request.LyDo = input.LyDo.Trim();
            request.DiaChiNoiNghi = input.DiaChiNoiNghi.Trim();
            LogUnsafe(session, "Sửa đơn", $"{request.HoTen} ({request.MaHocVien})");
            SaveUnsafe();
            return request.Clone();
        }
    }

    public List<LeaveRequest> GetFacultyRequests(
    AppSession session,
    string? className = null)
    {
        RequireRole(session, UserRole.BoMon);

        lock (_gate)
        {
            RefreshUnsafe();
            using var db = _store.Open();

            var subjects = db.ClassSubjects
                .AsNoTracking()
                .Where(x => x.FacultyUserId == session.UserId)
                .ToDictionary(x => x.Id);

            var result = new List<LeaveRequest>();

            foreach (var request in _data.Requests)
            {
                if (request.PhanLoaiNghi != LeaveCategories.TranhThu
                    || request.TrangThai != LeaveStatuses.Approved
                    || !request.SubjectId.HasValue)
                    continue;

                if (!subjects.TryGetValue(
                    request.SubjectId.Value, out var subject))
                    continue;

                if (!string.Equals(
                    request.Lop,
                    subject.ClassName,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(className)
                    && !request.Lop.Equals(
                        className.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var copy = request.Clone();
                copy.TagMonHoc = subject.Name;

                result.Add(copy);
            }

            return result
                .OrderByDescending(x => x.TuThoiDiem)
                .ThenByDescending(x => x.Id)
                .ToList();
        }
    }

    public void Delete(AppSession session, int id)
    {
        RequireRole(session, UserRole.TrungDoi);
        lock (_gate)
        {
            RefreshUnsafe();
            var request = FindUnsafe(id);
            if (session.Role == UserRole.TrungDoi)
                EnsureManagedClass(session, request.Lop);
            RequirePending(request);
            _data.Requests.Remove(request);
            LogUnsafe(session, "Xóa đơn", $"{request.HoTen} ({request.MaHocVien})");
            SaveUnsafe();
        }
    }

    private void EnsureNoOverlappingLeave(
    string maHocVien,
    DateTime tuThoiDiem,
    DateTime denThoiDiem,
    int? excludedId = null)
    {
        var existing = _data.Requests.FirstOrDefault(request =>
            request.MaHocVien.Equals(
                maHocVien,
                StringComparison.OrdinalIgnoreCase)
            && (!excludedId.HasValue || request.Id != excludedId.Value)
            && request.TrangThai != LeaveStatuses.Rejected && request.TrangThai != LeaveStatuses.Cancelled
            && tuThoiDiem < request.DenThoiDiem
            && denThoiDiem > request.TuThoiDiem);

        if (existing is not null)
        {
            throw new ArgumentException(
                $"Học viên {existing.HoTen} ({maHocVien}) "
                + $"đã có đơn {existing.MaDon} nghỉ từ "
                + $"{existing.TuThoiDiem:dd/MM/yyyy HH:mm} đến "
                + $"{existing.DenThoiDiem:dd/MM/yyyy HH:mm}. "
                + "Không thể tạo hoặc sửa đơn trùng thời gian.");
        }
    }

    public byte[] ExportExcel(AppSession session, IEnumerable<LeaveRequest> rows)
    {
        RequireLoggedIn(session);
        if (!session.CanExport)
            throw new UserAccessException("Vai trò hiện tại không được xuất Excel.");
        var requestedIds = rows.Select(x => x.Id).ToHashSet();
        return ExcelExporter.Build(GetRequests(session).Where(x => requestedIds.Contains(x.Id)).ToList());
    }

    private Student ValidateInput(LeaveInput input)
    {
        if (input.TrungDoi?.Length > 80)
            throw new ArgumentException("Tên trung đội tối đa 80 ký tự.");
        if (input.DenThoiDiem <= input.TuThoiDiem)
            throw new ArgumentException("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if ((input.DenThoiDiem - input.TuThoiDiem).TotalDays > 60)
            throw new ArgumentException("Đơn nghỉ không được vượt quá 60 ngày.");
        if (string.IsNullOrWhiteSpace(input.LyDo) || input.LyDo.Length > 500)
            throw new ArgumentException("Lý do nghỉ phải có nội dung và không vượt quá 500 ký tự.");
        if (string.IsNullOrWhiteSpace(input.DiaChiNoiNghi) || input.DiaChiNoiNghi.Length > 300)
            throw new ArgumentException("Địa chỉ nơi nghỉ phải có nội dung và không vượt quá 300 ký tự.");
        if (input.MaHocVien == "__manual__")
            throw new UserAccessException("Phòng QLHV cần thêm học viên vào danh mục trước khi trung đội lập đơn.");
        if (string.IsNullOrWhiteSpace(input.MaHocVien))
            throw new ArgumentException("Vui lòng chọn học viên.");
        var found = _data.Students.FirstOrDefault(x => x.MaHocVien.Equals(input.MaHocVien, StringComparison.OrdinalIgnoreCase) && x.IsActive)
            ?? throw new ArgumentException("Mã học viên không tồn tại.");
        if (string.IsNullOrWhiteSpace(input.TrungDoi))
            input.TrungDoi = string.IsNullOrWhiteSpace(found.TrungDoi) ? $"{LeaveCategories.PlatoonPrefix} {found.Lop}" : found.TrungDoi;
        return found;
    }
}
