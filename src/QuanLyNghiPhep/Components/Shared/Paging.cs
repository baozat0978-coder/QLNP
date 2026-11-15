namespace QuanLyNghiPhep.Components.Shared;

// Cắt danh sách theo trang cho <Pager>. Trang ngoài phạm vi (vd. sau khi lọc bớt dữ liệu)
// được kẹp về trang hợp lệ gần nhất, nên không bao giờ hiện bảng trống oan.
public static class Paging
{
    public const int DefaultSize = 10;
    public static readonly int[] Sizes = [10, 20, 50, 100];

    public static int TotalPages(int total, int size) => Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, size)));

    public static int Clamp(int total, int page, int size) => Math.Clamp(page, 1, TotalPages(total, size));

    /// <summary>Trả về các dòng của trang hiện tại và số thứ tự bắt đầu (để cột STT chạy liên tục giữa các trang).</summary>
    public static (List<T> Items, int Offset) Slice<T>(IEnumerable<T> source, int page, int size)
    {
        var all = source as IReadOnlyList<T> ?? source.ToList();
        var offset = (Clamp(all.Count, page, size) - 1) * size;
        return (all.Skip(offset).Take(size).ToList(), offset);
    }
}
