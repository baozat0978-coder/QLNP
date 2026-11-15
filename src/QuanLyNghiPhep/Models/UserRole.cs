namespace QuanLyNghiPhep.Models;

public enum UserRole
{
    TrungDoi,
    PhongQLHV,
    BoMon,
    Admin
}

public static class RoleLabels
{
    public static string Display(UserRole role) => role switch
    {
        UserRole.TrungDoi => "Chủ nhiệm trung đội",
        UserRole.PhongQLHV => "Phòng Quản lý học viên",
        UserRole.BoMon => "Giáo viên bộ môn",
        UserRole.Admin => "Quản trị viên",
        _ => "Không xác định"
    };
}
