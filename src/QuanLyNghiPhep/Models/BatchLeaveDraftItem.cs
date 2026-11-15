namespace QuanLyNghiPhep.Models;

public sealed class BatchLeaveDraftItem
{
    public int STT
    {
        get; set;
    }
    public string MaHocVien { get; set; } = "";
    public string HoTen { get; set; } = "";
    public string Lop { get; set; } = "";
    public string DiaChiNoiNghi { get; set; } = LeaveCategories.DefaultWeekendAddress;
    public DateTime TuThoiDiem
    {
        get; set;
    }
    public DateTime DenThoiDiem
    {
        get; set;
    }
    public bool IsEditingAddress
    {
        get; set;
    }
}
