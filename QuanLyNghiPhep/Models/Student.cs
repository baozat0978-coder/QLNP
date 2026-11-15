namespace QuanLyNghiPhep.Models;

public sealed class Student
{
    public bool IsActive { get; set; } = true;
    public int STT
    {
        get; set;
    }
    public string MaHocVien { get; set; } = "";
    public string HoTen { get; set; } = "";
    public string Lop { get; set; } = "";
    public string NgaySinh { get; set; } = "";
    public string SoDienThoai { get; set; } = "";
    public string TrungDoi { get; set; } = "";

    public Student Clone() => (Student)MemberwiseClone();
}
