using System.Text.Json;
using QuanLyNghiPhep.Data;
using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Models;

namespace QuanLyNghiPhep.Services.Business;

// Kho dữ liệu dùng chung, lưu SQLite; JSON cũ chỉ được đọc khi chuyển dữ liệu lần đầu.
// Mỗi thao tác ghi đều kiểm tra quyền ở tầng xử lý, không chỉ ẩn nút trên giao diện.
public sealed partial class LeaveRepository
{
    private readonly object _gate;
    private readonly string _file;
    private readonly BusinessStore _store;
    private readonly IServiceScopeFactory _scopes;
    private RepositoryData _data;
    private string? _committedJson;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LeaveRepository(IWebHostEnvironment env, IServiceScopeFactory scopes, BusinessStore store)
    {
        _scopes = scopes;
        string dir = Path.Combine(env.ContentRootPath, "AppData");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "du-lieu-nghi-phep.json");
        _store = store;
        _gate = store.SyncRoot;
        using var db = _store.Open();
        if (db.State.Any() || db.Classes.Any() || db.Students.Any() || db.Requests.Any())
        {
            _data = new RepositoryData { Classes = db.Classes.AsNoTracking().ToList(), Students = db.Students.AsNoTracking().ToList(), Requests = db.Requests.AsNoTracking().ToList(), Audit = new() };
            _data.NextId = db.State.AsNoTracking().FirstOrDefault()?.NextLeaveId ?? 1;
            NormalizeLoadedData(_data);
            InitializeManagers();
            _committedJson = JsonSerializer.Serialize(_data, JsonOptions);
            return;
        }
        if (File.Exists(_file))
        {
            _data = JsonSerializer.Deserialize<RepositoryData>(File.ReadAllText(_file))
                ?? throw new InvalidDataException("Không đọc được dữ liệu cũ; tệp gốc được giữ nguyên.");
            ValidateImportedData(_data);
            NormalizeLoadedData(_data);
            foreach (var audit in _data.Audit)
                audit.Id = 0;
            foreach (var r in _data.Requests.Where(r => !_data.Students.Any(s => s.MaHocVien == r.MaHocVien)))
                _data.Students.Add(new Student { MaHocVien = r.MaHocVien, HoTen = r.HoTen, Lop = r.Lop, TrungDoi = r.TrungDoi });
            NormalizeLoadedData(_data);
            SaveUnsafe();
        }
        else
        {
            _data = DemoData.Create();
            SaveUnsafe();
        }
        InitializeManagers();
        _committedJson = JsonSerializer.Serialize(_data, JsonOptions);
        File.WriteAllText(Path.Combine(dir, "business-migrated.txt"), "Dữ liệu nghiệp vụ đã chuyển sang business.db. JSON gốc được giữ nguyên làm bản sao lưu.");
    }

    private void RefreshUnsafe()
    {
        using var db = _store.Open();
        _data = new RepositoryData
        {
            Classes = db.Classes.AsNoTracking().ToList(),
            Students = db.Students.AsNoTracking().ToList(),
            Requests = db.Requests.AsNoTracking().ToList(),
            NextId = db.State.AsNoTracking().FirstOrDefault()?.NextLeaveId ?? 1
        };
        NormalizeLoadedData(_data);
        _committedJson = JsonSerializer.Serialize(_data, JsonOptions);
    }

    private string PlatoonOf(string code, string className)
        => _data.Students.FirstOrDefault(s => s.MaHocVien == code)?.TrungDoi is { Length: > 0 } t ? t : $"{LeaveCategories.PlatoonPrefix} {className}";

    private string CanonicalClass(string name)
        => _data.Classes.FirstOrDefault(x => x.TenLop.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))?.TenLop
        ?? throw new ArgumentException("Không tìm thấy lớp.");

    private static void ValidateImportedData(RepositoryData data)
    {
        if (data.Classes is null || data.Students is null || data.Requests is null || data.Audit is null)
            throw new InvalidDataException("Dữ liệu JSON cũ thiếu danh mục bắt buộc; tệp gốc được giữ nguyên.");
        if (data.Classes.Any(x => x is null || string.IsNullOrWhiteSpace(x.TenLop))
            || data.Classes.Select(x => x.TenLop).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Classes.Count
            || data.Students.Any(x => x is null || string.IsNullOrWhiteSpace(x.MaHocVien) || string.IsNullOrWhiteSpace(x.Lop))
            || data.Students.Select(x => x.MaHocVien).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Students.Count
            || data.Requests.Any(x => x is null || x.Id <= 0 || string.IsNullOrWhiteSpace(x.MaHocVien) || string.IsNullOrWhiteSpace(x.Lop) || x.DenThoiDiem <= x.TuThoiDiem)
            || data.Requests.Select(x => x.Id).Distinct().Count() != data.Requests.Count
            || data.Audit.Any(x => x is null))
            throw new InvalidDataException("Dữ liệu JSON cũ có khóa trùng hoặc hồ sơ không hợp lệ; cần kiểm tra bản sao lưu.");
    }

    private static bool NormalizeLoadedData(RepositoryData data)
    {
        bool changed = false;
        foreach (var s in data.Students)
        {
            if (!data.Classes.Any(c => c.TenLop.Equals(s.Lop, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(s.Lop))
            {
                data.Classes.Add(new ClassGroup { TenLop = s.Lop, NgayTao = DateTime.Now });
                changed = true;
            }
        }
        foreach (var student in data.Students)
        {
            var canonical = data.Classes.First(x => x.TenLop.Equals(student.Lop, StringComparison.OrdinalIgnoreCase)).TenLop;
            if (student.Lop != canonical)
            {
                student.Lop = canonical;
                changed = true;
            }
        }
        foreach (var request in data.Requests)
            if (!string.IsNullOrWhiteSpace(request.Lop) && !data.Classes.Any(x => x.TenLop.Equals(request.Lop, StringComparison.OrdinalIgnoreCase)))
            {
                data.Classes.Add(new ClassGroup { TenLop = request.Lop });
                changed = true;
            }
        int minimumNextId = data.Requests.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1;
        if (data.NextId < minimumNextId)
        {
            data.NextId = minimumNextId;
            changed = true;
        }
        return changed;
    }

    private (string Id, string Name) FindManager(string name, bool requireEnabled = true)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var normalized = name.ToUpperInvariant();
        var user = db.Users.AsNoTracking().SingleOrDefault(x => x.NormalizedUserName == normalized);
        if (user is null || (requireEnabled && !user.IsEnabled) || !db.UserRoles.Any(ur => ur.UserId == user.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == nameof(UserRole.TrungDoi))))
            throw new ArgumentException("Chọn tài khoản trung đội hợp lệ đang hoạt động.");
        return (user.Id, user.UserName ?? name);
    }

    private void InitializeManagers()
    {
        bool changed = false;
        foreach (var group in _data.Classes.Where(x => string.IsNullOrEmpty(x.ManagerUserId)))
        {
            if (string.IsNullOrWhiteSpace(group.NguoiTao))
                continue;
            try
            {
                group.ManagerUserId = FindManager(group.NguoiTao, false).Id;
                changed = true;
            }
            catch (ArgumentException) { /* Orphan assignment stays visible to staff for explicit reassignment. */ }
        }
        if (changed)
            SaveUnsafe();
    }

    private LeaveRequest FindUnsafe(int id) => _data.Requests.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("Không tìm thấy đơn nghỉ phép.");

    private void SaveUnsafe()
    {
        try
        {
            using var db = _store.Open();
            using var tx = db.Database.BeginTransaction();
            Sync(db, _data.Classes, (ClassGroup x) => x.TenLop);
            Sync(db, _data.Students, (Student x) => x.MaHocVien);
            Sync(db, _data.Requests, (LeaveRequest x) => x.Id.ToString());
            foreach (var entry in _data.Audit.Where(x => x.Id == 0))
                db.Audit.Add(entry);
            var state = db.State.SingleOrDefault();
            if (state is null)
                db.State.Add(new BusinessState { NextLeaveId = _data.NextId });
            else
                state.NextLeaveId = _data.NextId;
            db.SaveChanges();
            tx.Commit();
            _committedJson = JsonSerializer.Serialize(_data, JsonOptions);
        }
        catch
        {
            if (_committedJson is not null)
                _data = JsonSerializer.Deserialize<RepositoryData>(_committedJson)!;
            throw;
        }
    }

    private static void Sync<T>(BusinessDbContext db, List<T> values, Func<T, string> key) where T : class
    {
        var existing = db.Set<T>().ToList().ToDictionary(key);
        foreach (var value in values)
            if (existing.Remove(key(value), out var old))
                db.Entry(old).CurrentValues.SetValues(value);
            else
                db.Set<T>().Add(value);
        db.RemoveRange(existing.Values);
    }
}
