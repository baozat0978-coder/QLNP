namespace QuanLyNghiPhep.Models;

public sealed class LeaveRequest
{
    public int Id
    {
        get; set;
    }
    public string MaDon { get; set; } = "";
    public string MaHocVien { get; set; } = "";
    public string HoTen { get; set; } = "";
    public string Lop { get; set; } = "";
    public string TrungDoi { get; set; } = "";
    public DateTime TuThoiDiem
    {
        get; set;
    }
    public DateTime DenThoiDiem
    {
        get; set;
    }
    public string LyDo { get; set; } = "";
    public string DiaChiNoiNghi { get; set; } = "";
    public string TrangThai { get; set; } = LeaveStatuses.Approved;
    public int? SubjectId
    {
        get; set;
    }

    // Phân loại nghỉ & hệ thống Tag ẩn theo yêu cầu
    public string PhanLoaiNghi { get; set; } = LeaveCategories.TranhThu; // CuoiTuan | LeTet | TranhThu
    public string TagLoaiNghi { get; set; } = LeaveCategories.TagTranhThu; // Tag 1: Nghỉ phép cuối tuần | Nghỉ Tết / Nghỉ lễ 30-4 1-5... | Nghỉ phép tranh thủ
    public string TagMonHoc { get; set; } = ""; // Tag 2: Tên môn học (chỉ có khi Nghỉ phép tranh thủ, có thể nhiều môn cách nhau dấu phẩy)

    public string NguoiDuyetTrungDoi { get; set; } = "";
    public DateTime? NgayDuyetTrungDoi
    {
        get; set;
    }
    public string NguoiDuyetPhong { get; set; } = "";
    public DateTime? NgayDuyetPhong
    {
        get; set;
    }
    public DateTime NgayTao
    {
        get; set;
    }
    public string NguoiTao { get; set; } = "";
    public string NguoiDuyet { get; set; } = "";
    public DateTime? NgayDuyet
    {
        get; set;
    }
    public string GhiChuDuyet { get; set; } = "";

    public string LoaiDon => TuThoiDiem.Date == DenThoiDiem.Date ? "Trong ngày" : "Dài ngày";
    public double SoGio => Math.Max(0, (DenThoiDiem - TuThoiDiem).TotalHours);
    public double SoNgay => Math.Round(SoGio / 24.0, 2);

    // Hiển thị cột Môn học theo đúng quy tắc:
    // Nghỉ cuối tuần -> "Nghỉ cuối tuần"
    // Nghỉ lễ/tết -> "Nghỉ lễ/Tết"
    // Nghỉ tranh thủ -> Tag môn học đã nhập
    public string HienThiMonHoc => PhanLoaiNghi switch
    {
        LeaveCategories.CuoiTuan => "Nghỉ cuối tuần",
        LeaveCategories.LeTet => "Nghỉ lễ/Tết",
        _ => string.IsNullOrWhiteSpace(TagMonHoc) ? "Chưa gắn môn" : TagMonHoc
    };

    public List<string> DanhSachTagMon => PhanLoaiNghi == LeaveCategories.TranhThu && !string.IsNullOrWhiteSpace(TagMonHoc)
        ? TagMonHoc.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
        : new List<string>();

    public LeaveRequest Clone() => (LeaveRequest)MemberwiseClone();
}
