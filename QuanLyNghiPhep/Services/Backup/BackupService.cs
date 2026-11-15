using System.IO.Compression;
using Microsoft.Data.Sqlite;
using QuanLyNghiPhep.Models;
using QuanLyNghiPhep.Services.Accounts;

namespace QuanLyNghiPhep.Services.Backup;

// Sao lưu: VACUUM INTO tạo bản sao nhất quán khi app đang chạy. Khôi phục: đặt tệp chờ, áp dụng khi khởi động lại.
public sealed class BackupService(IWebHostEnvironment env)
{
    private static readonly string[] DbFiles = ["accounts.db", "business.db"];
    private string Dir => Path.Combine(env.ContentRootPath, "AppData");
    private string BackupDir => Path.Combine(Dir, "backups");
    private string PendingDir => Path.Combine(Dir, "restore-pending");
    private readonly SemaphoreSlim gate = new(1, 1);

    private static void RequireAdmin(AppSession s)
    {
        s.EnsureValid();
        if (s.Role != UserRole.Admin)
            throw new UserAccessException("Chỉ Admin được sao lưu/khôi phục.");
    }

    public List<FileInfo> List()
        => Directory.Exists(BackupDir) ? new DirectoryInfo(BackupDir).GetFiles("*.zip").OrderByDescending(x => x.CreationTime).ToList() : new();

    public bool HasPendingRestore => Directory.Exists(PendingDir);

    public string CreateBackup(AppSession session) { RequireAdmin(session); return CreateBackupCore("sao-luu"); }

    public string CreateBackupCore(string prefix)
    {
        gate.Wait();
        try
        {
            Directory.CreateDirectory(BackupDir);
            var temp = Path.Combine(Path.GetTempPath(), "qlnp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                foreach (var name in DbFiles)
                {
                    var src = Path.Combine(Dir, name);
                    if (!File.Exists(src)) continue;
                    var dst = Path.Combine(temp, name);
                    using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = src, Mode = SqliteOpenMode.ReadWrite }.ToString());
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"VACUUM INTO '{dst.Replace("'", "''")}'";
                    cmd.ExecuteNonQuery();
                }
                var zipPath = Path.Combine(BackupDir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                ZipFile.CreateFromDirectory(temp, zipPath);
                return zipPath;
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }
        finally { gate.Release(); }
    }

    public byte[] Read(AppSession session, string fileName)
    {
        RequireAdmin(session);
        var path = Path.Combine(BackupDir, Path.GetFileName(fileName));
        return File.Exists(path) ? File.ReadAllBytes(path) : throw new ArgumentException("Không tìm thấy bản sao lưu.");
    }

    public void StageRestore(AppSession session, Stream zip)
    {
        RequireAdmin(session);
        var temp = Path.Combine(Dir, "restore-staging");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        try
        {
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
                foreach (var name in DbFiles)
                {
                    var entry = archive.GetEntry(name) ?? throw new ArgumentException($"Tệp sao lưu thiếu {name}.");
                    entry.ExtractToFile(Path.Combine(temp, name));
                }
            foreach (var name in DbFiles)
            {
                var header = new byte[16];
                using var fs = File.OpenRead(Path.Combine(temp, name));
                if (fs.Read(header, 0, 16) < 16 || !System.Text.Encoding.ASCII.GetString(header).StartsWith("SQLite format 3"))
                    throw new ArgumentException($"{name} không phải database hợp lệ.");
            }
            if (Directory.Exists(PendingDir)) Directory.Delete(PendingDir, true);
            Directory.Move(temp, PendingDir);
        }
        catch { if (Directory.Exists(temp)) Directory.Delete(temp, true); throw; }
    }

    // Gọi ngay khi khởi động, trước khi mở bất kỳ database nào.
    public static void ApplyPendingRestore(string dataDir)
    {
        var pending = Path.Combine(dataDir, "restore-pending");
        if (!Directory.Exists(pending) || !DbFiles.All(n => File.Exists(Path.Combine(pending, n)))) return;
        var backups = Path.Combine(dataDir, "backups");
        Directory.CreateDirectory(backups);
        var before = Path.Combine(backups, $"truoc-khoi-phuc-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using (var zip = ZipFile.Open(before, ZipArchiveMode.Create))
            foreach (var n in DbFiles)
                if (File.Exists(Path.Combine(dataDir, n)))
                    zip.CreateEntryFromFile(Path.Combine(dataDir, n), n);
        foreach (var n in DbFiles)
        {
            foreach (var ext in new[] { "", "-wal", "-shm" })
                if (File.Exists(Path.Combine(dataDir, n + ext))) File.Delete(Path.Combine(dataDir, n + ext));
            File.Copy(Path.Combine(pending, n), Path.Combine(dataDir, n));
        }
        Directory.Delete(pending, true);
    }

    public void Prune(int keep = 14)
    {
        foreach (var f in List().Where(x => x.Name.StartsWith("tu-dong-")).Skip(keep))
            try { f.Delete(); } catch { }
    }
}
