namespace QuanLyNghiPhep.Models;

public static class LeaveCategories
{
    public const string CuoiTuan = "CuoiTuan";
    public const string LeTet = "LeTet";
    public const string TranhThu = "TranhThu";

    public static string DefaultWeekendAddress { get; set; } = "Phường Hà Đông, Thành phố Hà Nội";
    public static string PlatoonPrefix { get; set; } = "Trung đội";
    public static int TranhThuMonthlyLimit { get; set; } = 3;
    public const string TagCuoiTuan = "Nghỉ phép cuối tuần";
    public const string TagTranhThu = "Nghỉ phép tranh thủ";
}
