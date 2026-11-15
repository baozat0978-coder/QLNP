namespace QuanLyNghiPhep.Models;

public sealed class LeaveInput
{
    public string MaHocVien { get; set; } = "";
    public string TrungDoi { get; set; } = "";
    public DateTime TuThoiDiem { get; set; } = DateTime.Today.AddHours(7);
    public DateTime DenThoiDiem { get; set; } = DateTime.Today.AddHours(17);
    public string LyDo { get; set; } = "";
    public string DiaChiNoiNghi { get; set; } = LeaveCategories.DefaultWeekendAddress;
    public string PhanLoaiNghi { get; set; } = LeaveCategories.TranhThu;
    public string TagLoaiNghi { get; set; } = LeaveCategories.TagTranhThu;
    public string TagMonHoc { get; set; } = "";

    public static LeaveInput From(LeaveRequest item) => new()
    {
        MaHocVien = item.MaHocVien,
        TrungDoi = item.TrungDoi,
        TuThoiDiem = item.TuThoiDiem,
        DenThoiDiem = item.DenThoiDiem,
        LyDo = item.LyDo,
        DiaChiNoiNghi = item.DiaChiNoiNghi,
        PhanLoaiNghi = item.PhanLoaiNghi,
        TagLoaiNghi = item.TagLoaiNghi,
        TagMonHoc = item.TagMonHoc
    };
}
