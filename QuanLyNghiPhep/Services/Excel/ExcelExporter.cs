using System.IO.Compression;
using System.Globalization;
using System.Xml;
using Microsoft.VisualBasic.FileIO;
using System.Security;
using System.Text;
using System.Xml.Linq;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services.Excel;

// Tạo tệp Excel .xlsx chuẩn OpenXML bằng thư viện hệ thống, không cần tải NuGet.
public static class ExcelExporter
{
    public static byte[] BuildTable(string sheet, IReadOnlyList<string[]> rows)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int i = 0; i < rows.Count; i++)
            WriteRow(xml, i + 1, rows[i]);
        xml.Append("</sheetData></worksheet>");
        return BuildWorkbook(sheet, xml.ToString());
    }
    private static readonly string[] Headers =
    {
        "STT", "Mã học viên", "Họ và tên", "Lớp", "Loại nghỉ (Tag 1)", "Môn học (Tag 2)",
        "Bắt đầu nghỉ", "Kết thúc nghỉ", "Số giờ nghỉ",
        "Lý do nghỉ", "Địa chỉ nơi nghỉ", "Trạng thái", "Ngày lập", "Người duyệt"
    };

    private static readonly string[] ClassHeaders =
    {
        "STT", "Tên", "Lớp", "Ngày sinh", "Số điện thoại"
    };

    public static byte[] Build(IReadOnlyList<LeaveRequest> records)
    {
        return BuildWorkbook("Danh sach nghi phep", Worksheet(records));
    }

    public static byte[] BuildClassTemplate(string className, IReadOnlyList<Student> students)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        WriteRow(xml, 1, ClassHeaders);
        if (students.Count > 0)
        {
            for (int i = 0; i < students.Count; i++)
            {
                var s = students[i];
                WriteRow(xml, i + 2, new[]
                {
                    (i + 1).ToString(), s.HoTen, string.IsNullOrWhiteSpace(s.Lop) ? className : s.Lop, s.NgaySinh, s.SoDienThoai
                });
            }
        }
        else
        {
            WriteRow(xml, 2, new[] { "1", "Nguyễn Văn An", string.IsNullOrWhiteSpace(className) ? "B9D55" : className, "15/05/2005", "0912345678" });
            WriteRow(xml, 3, new[] { "2", "Trần Minh Đức", string.IsNullOrWhiteSpace(className) ? "B9D55" : className, "20/08/2005", "0987654321" });
        }
        xml.Append("</sheetData></worksheet>");
        return BuildWorkbook("Danh sach lop", xml.ToString());
    }

    private static byte[] BuildWorkbook(string sheetName, string worksheetXml)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Write(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Write(zip, "xl/workbook.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="{EscapeCell(sheetName)}" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Write(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            Write(zip, "xl/worksheets/sheet1.xml", worksheetXml);
        }
        return output.ToArray();
    }

    private static string Worksheet(IReadOnlyList<LeaveRequest> records)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        WriteRow(xml, 1, Headers);
        for (int i = 0; i < records.Count; i++)
        {
            var x = records[i];
            WriteRow(xml, i + 2, new string[]
            {
                (i + 1).ToString(), x.MaHocVien, x.HoTen, x.Lop,
                x.TagLoaiNghi, x.HienThiMonHoc, x.TuThoiDiem.ToString("dd/MM/yyyy HH:mm"),
                x.DenThoiDiem.ToString("dd/MM/yyyy HH:mm"), x.SoGio.ToString("0.##"),
                x.LyDo, x.DiaChiNoiNghi, x.TrangThai,
                x.NgayTao.ToString("dd/MM/yyyy HH:mm"), x.NguoiDuyet
            });
        }
        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    private static void WriteRow(StringBuilder xml, int number, IEnumerable<string> cells)
    {
        xml.Append("<row r=\"").Append(number).Append("\">");
        int col = 0;
        foreach (var value in cells)
        {
            xml.Append("<c r=\"").Append(ColumnName(col++)).Append(number)
                .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(EscapeCell(value))
                .Append("</t></is></c>");
        }
        xml.Append("</row>");
    }

    private static string EscapeCell(string? value)
    {
        var text = new StringBuilder();
        foreach (var rune in (value ?? "").EnumerateRunes())
            if (rune.Value is 9 or 10 or 13 or (>= 0x20 and <= 0xD7FF)
                or (>= 0xE000 and <= 0xFFFD) or (>= 0x10000 and <= 0x10FFFF))
                text.Append(rune);
        return SecurityElement.Escape(text.ToString()) ?? "";
    }

    private static string ColumnName(int zeroBased)
    {
        var result = "";
        for (int x = zeroBased + 1; x > 0; x = (x - 1) / 26)
            result = (char)('A' + (x - 1) % 26) + result;
        return result;
    }

    private static void Write(ZipArchive zip, string name, string contents)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }
}
