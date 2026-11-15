using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Services.Business;

// Luồng duyệt đơn: duyệt, từ chối, duyệt hàng loạt và huỷ.
public sealed partial class LeaveRepository
{
    public LeaveRequest Review(AppSession session, int id, bool approved, string note)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.TrungDoi or UserRole.PhongQLHV))
            throw new UserAccessException("Vai trò này không có quyền xét duyệt.");
        lock (_gate)
        {
            RefreshUnsafe();
            var request = FindUnsafe(id);
            if (session.Role == UserRole.TrungDoi)
                EnsureManagedClass(session, request.Lop);
            bool isTrungDoi = session.Role == UserRole.TrungDoi;
            string required = isTrungDoi ? LeaveStatuses.WaitingTrungDoi : LeaveStatuses.WaitingPhong;
            if (request.TrangThai != required)
                throw new BusinessRuleException(isTrungDoi
                    ? "Đơn đã qua bước xét duyệt trung đội."
                    : "Đơn chưa được trung đội duyệt hoặc đã được xử lý.");
            if (!approved && string.IsNullOrWhiteSpace(note))
                throw new ArgumentException("Cần ghi lý do khi từ chối đơn.");
            if (note.Length > 300)
                throw new ArgumentException("Ghi chú xét duyệt không vượt quá 300 ký tự.");
            var now = DateTime.Now;
            if (isTrungDoi)
            {
                request.NguoiDuyetPhong = "";
                request.NgayDuyetPhong = null;
                request.NguoiDuyetTrungDoi = session.UserName;
                request.NgayDuyetTrungDoi = now;
                request.TrangThai = approved ? LeaveStatuses.WaitingPhong : LeaveStatuses.Rejected;
            }
            else
            {
                if (!request.NgayDuyetTrungDoi.HasValue)
                    throw new BusinessRuleException("Thiếu xác nhận của giáo viên trung đội.");
                request.NguoiDuyetPhong = session.UserName;
                request.NgayDuyetPhong = now;
                request.TrangThai = approved ? LeaveStatuses.Approved : LeaveStatuses.Rejected;
            }
            request.NguoiDuyet = session.UserName;
            request.NgayDuyet = now;
            request.GhiChuDuyet = note.Trim();
            LogUnsafe(session, isTrungDoi
                ? (approved ? "Trung đội duyệt" : "Trung đội từ chối")
                : (approved ? "Phòng QLHV duyệt" : "Phòng QLHV từ chối"),
                $"{request.HoTen} ({request.MaHocVien})");
            SaveUnsafe();
            return request.Clone();
        }
    }

    // Phòng QLHV duyệt tất cả đơn đang chờ của 1 lớp theo 1 loại phép (cuối tuần / lễ-tết).
    public int ApproveAllPending(AppSession session, string lop, string phanLoai)
    {
        RequireLoggedIn(session);
        if (session.Role != UserRole.PhongQLHV)
            throw new UserAccessException("Chỉ Phòng QLHV được duyệt hàng loạt.");
        if (phanLoai == LeaveCategories.TranhThu)
            throw new ArgumentException("Đơn tranh thủ phải duyệt từng đơn.");
        lock (_gate)
        {
            RefreshUnsafe();
            var now = DateTime.Now;
            var list = _data.Requests.Where(x => x.TrangThai == LeaveStatuses.WaitingPhong
                && x.PhanLoaiNghi == phanLoai
                && x.Lop.Equals(lop, StringComparison.OrdinalIgnoreCase)
                && x.NgayDuyetTrungDoi.HasValue).ToList();
            if (list.Count == 0) return 0;
            foreach (var request in list)
            {
                request.NguoiDuyetPhong = session.UserName;
                request.NgayDuyetPhong = now;
                request.TrangThai = LeaveStatuses.Approved;
                request.NguoiDuyet = session.UserName;
                request.NgayDuyet = now;
            }
            LogUnsafe(session, "Phòng QLHV duyệt tất cả", $"Lớp {lop}: {list.Count} đơn ({phanLoai})");
            SaveUnsafe();
            return list.Count;
        }
    }

    public LeaveRequest Cancel(AppSession session, int id, string reason)
    {
        RequireLoggedIn(session);
        if (session.Role is not (UserRole.TrungDoi or UserRole.PhongQLHV))
            throw new UserAccessException("Vai trò này không có quyền hủy đơn.");
        reason = reason?.Trim() ?? "";
        if (reason.Length is < 1 or > 300)
            throw new ArgumentException("Cần ghi lý do hủy (tối đa 300 ký tự).");
        lock (_gate)
        {
            RefreshUnsafe();
            var request = FindUnsafe(id);
            if (session.Role == UserRole.TrungDoi)
                EnsureManagedClass(session, request.Lop);
            if (request.TrangThai is not (LeaveStatuses.Approved or LeaveStatuses.WaitingPhong))
                throw new BusinessRuleException("Chỉ hủy được đơn đang chờ Phòng QLHV hoặc đã duyệt.");
            var now = DateTime.Now;
            request.TrangThai = LeaveStatuses.Cancelled;
            request.NguoiDuyet = session.UserName;
            request.NgayDuyet = now;
            request.GhiChuDuyet = "Hủy: " + reason;
            LogUnsafe(session, "Hủy đơn", $"{request.MaDon} {request.HoTen} ({request.MaHocVien}) - Lý do: {reason}");
            SaveUnsafe();
            return request.Clone();
        }
    }

    private static void RequirePending(LeaveRequest request)
    {
        if (request.TrangThai != LeaveStatuses.WaitingTrungDoi)
            throw new BusinessRuleException("Đơn đã được xét duyệt, không thể sửa, xóa hoặc xét duyệt lần nữa.");
    }
}
