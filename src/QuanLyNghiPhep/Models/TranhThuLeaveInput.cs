namespace QuanLyNghiPhep.Models;

public sealed class TranhThuLeaveInput
{
    public string TenHocVien { get; set; } = "";
    public string MaHocVien { get; set; } = "";
    public string Lop { get; set; } = "";
    public string LyDo { get; set; } = "";
    public string DiaChiNoiNghi { get; set; } = LeaveCategories.DefaultWeekendAddress;
    public DateTime TuThoiDiem { get; set; } = DateTime.Today.AddHours(7);
    public DateTime DenThoiDiem { get; set; } = DateTime.Today.AddHours(17);
    public string MonHoc { get; set; } = "";
    public int? SubjectId
    {
        get; set;
    }
}
