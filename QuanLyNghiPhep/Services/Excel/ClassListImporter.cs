using System.IO.Compression;
using System.Globalization;
using System.Xml;
using Microsoft.VisualBasic.FileIO;
using System.Security;
using System.Text;
using System.Xml.Linq;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services.Excel;

// Đọc danh sách lớp từ tệp .xlsx (OpenXML) hoặc CSV/TXT bằng thư viện hệ thống.
public static class ClassListImporter
{
    public static List<Student> ParseClassExcel(Stream stream, string fallbackClass = "")
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > 10 * 1024 * 1024)
                throw new InvalidDataException("Tệp nhập không được lớn hơn 10 MB.");
            memory.Write(buffer, 0, read);
        }
        var bytes = memory.ToArray();
        List<string[]> rows;
        bool date1904 = false;
        if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            memory.Position = 0;
            rows = ReadWorkbook(memory, out date1904);
        }
        else
            rows = ReadDelimited(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        return ConvertRows(rows, fallbackClass, date1904);
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 20 * 1024 * 1024
        });
        try
        {
            return XDocument.Load(reader);
        }
        catch (XmlException error) { throw new InvalidDataException("Nội dung XML trong tệp Excel không hợp lệ.", error); }
    }

    private static List<string[]> ReadWorkbook(Stream stream, out bool date1904)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > 1000 || zip.Entries.Sum(e => e.Length) > 20 * 1024 * 1024)
            throw new InvalidDataException("Tệp Excel vượt giới hạn giải nén 20 MB / 1000 thành phần.");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbook = ReadXml(zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("Thiếu workbook Excel."));
        date1904 = workbook.Root?.Element(main + "workbookPr")?.Attribute("date1904")?.Value is "1" or "true";
        var firstSheet = workbook.Descendants(main + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException("Không tìm thấy sheet dữ liệu.");
        var relations = ReadXml(zip.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("Thiếu liên kết sheet."));
        var sheetRel = relations.Descendants(packageRel + "Relationship").SingleOrDefault(x => x.Attribute("Id")?.Value == firstSheet.Attribute(rel + "id")?.Value)
            ?? throw new InvalidDataException("Liên kết sheet không hợp lệ.");
        if (sheetRel.Attribute("TargetMode")?.Value == "External")
            throw new InvalidDataException("Không đọc sheet từ liên kết bên ngoài.");
        var target = sheetRel.Attribute("Target")?.Value ?? "";
        var sheetPath = new Uri(new Uri("https://workbook.invalid/xl/workbook.xml"), target).AbsolutePath.TrimStart('/');
        var sheet = ReadXml(zip.GetEntry(sheetPath) ?? throw new InvalidDataException("Không tìm thấy sheet đầu tiên."));
        var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
        var shared = sharedEntry is null ? new List<string>() : ReadXml(sharedEntry).Descendants(main + "si")
            .Select(x => string.Concat(x.Descendants(main + "t").Select(t => t.Value))).ToList();
        var rows = new List<string[]>();
        foreach (var row in sheet.Descendants(main + "row"))
        {
            var values = new Dictionary<int, string>();
            int sequential = 0;
            foreach (var cell in row.Elements(main + "c"))
            {
                int column = cell.Attribute("r") is { } reference ? ParseColumnIndex(reference.Value) : sequential;
                sequential = column + 1;
                if (column >= 32)
                    continue;
                var type = cell.Attribute("t")?.Value;
                var value = type == "inlineStr" ? string.Concat(cell.Descendants(main + "t").Select(x => x.Value))
                    : cell.Element(main + "v")?.Value ?? "";
                if (type == "s")
                {
                    if (!int.TryParse(value, out var index) || index < 0 || index >= shared.Count)
                        throw new InvalidDataException("Ô văn bản Excel không hợp lệ.");
                    value = shared[index];
                }
                values[column] = value.Trim();
            }
            if (values.Values.All(string.IsNullOrWhiteSpace))
                continue;
            var cells = new string[values.Keys.Max() + 1];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = values.GetValueOrDefault(i, "");
            rows.Add(cells);
            if (rows.Count > ValidationRules.MaxImportRows + 1)
                throw new InvalidDataException($"Chỉ nhập tối đa {ValidationRules.MaxImportRows} học viên mỗi lần.");
        }
        return rows;
    }

    private static List<string[]> ReadDelimited(string text)
    {
        var firstLine = text.Split('\n')[0];
        var separators = new Dictionary<char, int> { ['\t'] = 0, [';'] = 0, [','] = 0 };
        bool quoted = false;
        foreach (var ch in firstLine)
        {
            if (ch == '"')
                quoted = !quoted;
            else if (!quoted && separators.ContainsKey(ch))
                separators[ch]++;
        }
        var separator = separators.OrderByDescending(x => x.Value).First().Key;
        using var parser = new TextFieldParser(new StringReader(text)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(separator.ToString());
        var rows = new List<string[]>();
        try
        {
            while (!parser.EndOfData)
            {
                var row = parser.ReadFields() ?? [];
                if (row.All(string.IsNullOrWhiteSpace))
                    continue;
                rows.Add(row.Select(x => x.Trim()).ToArray());
                if (rows.Count > ValidationRules.MaxImportRows + 1)
                    throw new InvalidDataException($"Chỉ nhập tối đa {ValidationRules.MaxImportRows} học viên mỗi lần.");
            }
        }
        catch (MalformedLineException error) { throw new InvalidDataException($"Dòng {parser.ErrorLineNumber}: CSV không đúng cú pháp.", error); }
        return rows;
    }

    private static string HeaderKey(string value)
        => string.Concat(value.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c)).Where(char.IsLetterOrDigit));

    private static List<Student> ConvertRows(List<string[]> rows, string fallbackClass, bool date1904)
    {
        if (rows.Count == 0)
            return [];

        int headerRow = -1;
        int nameCol = -1, classCol = -1, dateCol = -1, phoneCol = -1, codeCol = -1;
        string detectedClass = fallbackClass;

        // 1. Quét các dòng đầu để tự động tìm dòng tiêu đề thật (Họ tên, Ngày sinh, SĐT...)
        for (int r = 0; r < Math.Min(rows.Count, 15); r++)
        {
            var headers = rows[r].Select(HeaderKey).ToList();
            int Index(params string[] names) => headers.FindIndex(h => names.Any(n => h.Contains(n)));

            int n = Index("ten", "hoten", "hovaten", "tenhocvien");
            int d = Index("ngaysinh", "ngaythangnamsinh", "ns");
            int p = Index("sodienthoai", "dienthoai", "sdt", "phone");

            if (n >= 0 && (d >= 0 || p >= 0))
            {
                headerRow = r;
                nameCol = n;
                dateCol = d;
                phoneCol = p;
                classCol = Index("lop", "tenlop");
                codeCol = Index("mahocvien", "mahv", "masv", "sohieu");
                break;
            }

            // Tự động nhận diện tên lớp nếu gặp dòng như "DANH SÁCH LỚP B10D57"
            if (string.IsNullOrWhiteSpace(detectedClass))
            {
                foreach (var cell in rows[r])
                {
                    var match = System.Text.RegularExpressions.Regex.Match(cell, @"(?:Lớp|lop|LỚP)\s*[:\s]*([A-Za-z0-9\-_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        detectedClass = match.Groups[1].Value.Trim();
                    }
                }
            }
        }

        // Nếu file không có dòng tiêu đề cột, dùng vị trí mặc định
        if (headerRow < 0)
        {
            int offset = rows[0].Length == 4 ? 0 : 1;
            nameCol = offset;
            classCol = offset + 1;
            dateCol = offset + 2;
            phoneCol = offset + 3;
            headerRow = -1;
        }

        var result = new List<Student>();

        // 2. Đọc toàn bộ học viên bắt đầu từ sau dòng tiêu đề
        for (int r = headerRow + 1; r < rows.Count; r++)
        {
            string Cell(int column) => column >= 0 && column < rows[r].Length ? rows[r][column].Trim() : "";
            var name = Cell(nameCol);

            // Bỏ qua dòng trống hoặc dòng chữ lặp lại
            if (string.IsNullOrWhiteSpace(name) || name.Equals("Họ và tên", StringComparison.OrdinalIgnoreCase) || name.StartsWith("DANH SÁCH", StringComparison.OrdinalIgnoreCase))
                continue;

            var date = Cell(dateCol);
            if (double.TryParse(date, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial > 10000 && serial < 100000)
                date = DateTime.FromOADate(serial + (date1904 ? 1462 : 0)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            else if (DateTime.TryParseExact(date, ["d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                date = parsed.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            string studentClass = classCol >= 0 && !string.IsNullOrWhiteSpace(Cell(classCol)) ? Cell(classCol) : detectedClass.Trim();

            result.Add(new Student
            {
                STT = result.Count + 1,
                MaHocVien = Cell(codeCol),
                HoTen = name,
                Lop = studentClass,
                NgaySinh = date,
                SoDienThoai = Cell(phoneCol)
            });
        }

        if (result.Count > ValidationRules.MaxImportRows)
            throw new InvalidDataException($"Chỉ nhập tối đa {ValidationRules.MaxImportRows} học viên.");

        return result;
    }

    private static int ParseColumnIndex(string reference)
    {
        int column = 0, letters = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch))
                break;
            if (++letters > 3 || char.ToUpperInvariant(ch) is < 'A' or > 'Z')
                throw new InvalidDataException("Địa chỉ ô Excel không hợp lệ.");
            column = column * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
        }
        if (column is < 1 or > 16384)
            throw new InvalidDataException("Cột Excel vượt giới hạn.");
        return column - 1;
    }
}
