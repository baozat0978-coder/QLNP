namespace QuanLyNghiPhep.Services.Backup;

public sealed class DailyBackupService(BackupService backup, ILogger<DailyBackupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!backup.List().Any(x => x.Name.StartsWith("tu-dong-") && x.CreationTime > DateTime.Now.AddHours(-23)))
                {
                    backup.CreateBackupCore("tu-dong");
                    backup.Prune();
                }
            }
            catch (Exception ex) { logger.LogError(ex, "Sao lưu tự động thất bại"); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
