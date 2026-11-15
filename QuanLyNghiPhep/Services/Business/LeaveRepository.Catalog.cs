using Microsoft.EntityFrameworkCore;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Services.Business;

// Danh mục lễ Tết và danh mục trung đội.
public sealed partial class LeaveRepository
{
    public List<Holiday> GetHolidays(AppSession session)
    {
        RequireLoggedIn(session);
        lock (_gate) { using var db = _store.Open(); return db.Holidays.AsNoTracking().OrderBy(x => x.FromDate).ToList(); }
    }

    public void AddHoliday(AppSession session, string name, DateTime from, DateTime to)
    {
        RequireClassStaff(session);
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 300) throw new ArgumentException("Tên đợt nghỉ tối đa 300 ký tự.");
        if (to <= from || (to - from).TotalDays > 60) throw new ArgumentException("Thời gian kết thúc phải sau bắt đầu và không quá 60 ngày.");
        lock (_gate)
        {
            RefreshUnsafe();
            using (var db = _store.Open()) { db.Holidays.Add(new Holiday { Name = name, FromDate = from, ToDate = to }); db.SaveChanges(); }
            LogUnsafe(session, "Thêm lịch nghỉ lễ/Tết", $"{name}: {from:dd/MM/yyyy} - {to:dd/MM/yyyy}");
            SaveUnsafe();
        }
    }

    public void DeleteHoliday(AppSession session, int id)
    {
        RequireClassStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            using (var db = _store.Open())
            {
                var h = db.Holidays.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Không tìm thấy đợt nghỉ.");
                db.Holidays.Remove(h); db.SaveChanges();
                LogUnsafe(session, "Xóa lịch nghỉ lễ/Tết", h.Name);
            }
            SaveUnsafe();
        }
    }

    public List<string> GetPlatoons(AppSession session)
    {
        RequireLoggedIn(session);
        lock (_gate) { using var db = _store.Open(); return db.Platoons.AsNoTracking().Select(x => x.Name).OrderBy(x => x).ToList(); }
    }

    public void AddPlatoon(AppSession session, string name)
    {
        RequireClassStaff(session);
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 80) throw new ArgumentException("Tên trung đội tối đa 80 ký tự.");
        lock (_gate)
        {
            RefreshUnsafe();
            using (var db = _store.Open())
            {
                if (db.Platoons.Any(x => x.Name == name)) throw new ArgumentException("Trung đội đã có trong danh mục.");
                db.Platoons.Add(new PlatoonItem { Name = name }); db.SaveChanges();
            }
            LogUnsafe(session, "Thêm trung đội vào danh mục", name);
            SaveUnsafe();
        }
    }

    public void DeletePlatoon(AppSession session, string name)
    {
        RequireClassStaff(session);
        lock (_gate)
        {
            RefreshUnsafe();
            if (_data.Students.Any(x => x.TrungDoi == name))
                throw new BusinessRuleException("Trung đội đang được gán cho học viên nên không thể xóa.");
            using (var db = _store.Open())
            {
                var p = db.Platoons.SingleOrDefault(x => x.Name == name) ?? throw new ArgumentException("Không tìm thấy trung đội.");
                db.Platoons.Remove(p); db.SaveChanges();
            }
            LogUnsafe(session, "Xóa trung đội khỏi danh mục", name);
            SaveUnsafe();
        }
    }
}
