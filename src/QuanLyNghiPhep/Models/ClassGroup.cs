namespace QuanLyNghiPhep.Models;

public sealed class ClassGroup
{
    public string TenLop { get; set; } = "";
    public string ManagerUserId { get; set; } = "";
    public string NguoiTao { get; set; } = "";
    public DateTime NgayTao { get; set; } = DateTime.Now;
}
