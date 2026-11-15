namespace QuanLyNghiPhep.Services;

public enum ToastKind { Success, Error, Info }

public sealed record Toast(Guid Id, ToastKind Kind, string Title, string Message);

public sealed record ConfirmRequest(string Title, string Message, string ConfirmText, bool Danger);

// Thông báo dùng chung cho toàn ứng dụng (mỗi phiên Blazor một bản):
// - toast góc phải sau khi thêm/sửa/xóa thành công hoặc thất bại;
// - hộp xác nhận trước các thao tác xóa.
// Giao diện do Components/Shared/NotifyHost.razor vẽ.
public sealed class NotifyService
{
    private const int SuccessMs = 4000;
    private const int MaxToasts = 5;

    private readonly List<Toast> _toasts = new();
    private readonly object _gate = new();
    private TaskCompletionSource<bool>? _confirm;

    public event Action? Changed;

    public IReadOnlyList<Toast> Toasts { get { lock (_gate) return _toasts.ToList(); } }
    public ConfirmRequest? PendingConfirm { get; private set; }

    public void Success(string? message, string title = "Thành công") => Show(ToastKind.Success, title, message, SuccessMs);
    public void Info(string? message, string title = "Thông báo") => Show(ToastKind.Info, title, message, SuccessMs);
    // Lỗi không tự ẩn: người dùng cần đọc rồi tự đóng
    public void Error(string? message, string title = "Không thực hiện được") => Show(ToastKind.Error, title, message, 0);

    public void Dismiss(Guid id)
    {
        lock (_gate)
            if (_toasts.RemoveAll(x => x.Id == id) == 0) return;
        Changed?.Invoke();
    }

    /// <summary>Đóng các toast lỗi đang mở (khi người dùng thao tác lại).</summary>
    public void ClearErrors()
    {
        lock (_gate)
            if (_toasts.RemoveAll(x => x.Kind == ToastKind.Error) == 0) return;
        Changed?.Invoke();
    }

    /// <summary>Hiện hộp xác nhận; trả về true nếu người dùng bấm nút xác nhận.</summary>
    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Xóa", bool danger = true)
    {
        _confirm?.TrySetResult(false); // hộp cũ (nếu còn) coi như bị hủy
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirm = tcs;
        PendingConfirm = new ConfirmRequest(title, message, confirmText, danger);
        Changed?.Invoke();
        return tcs.Task;
    }

    public void ResolveConfirm(bool ok)
    {
        var tcs = _confirm;
        _confirm = null;
        PendingConfirm = null;
        Changed?.Invoke();
        tcs?.TrySetResult(ok);
    }

    private void Show(ToastKind kind, string title, string? message, int autoHideMs)
    {
        if (string.IsNullOrWhiteSpace(message)) return; // OnNotice("") chỉ để xóa thông báo cũ
        var toast = new Toast(Guid.NewGuid(), kind, title, message.Trim());
        lock (_gate)
        {
            // Không lặp lại đúng thông báo đang hiển thị
            if (_toasts.Any(x => x.Kind == kind && x.Message == toast.Message)) return;
            _toasts.Add(toast);
            if (_toasts.Count > MaxToasts) _toasts.RemoveAt(0);
        }
        Changed?.Invoke();
        if (autoHideMs > 0)
            _ = Task.Delay(autoHideMs).ContinueWith(_ => Dismiss(toast.Id), TaskScheduler.Default);
    }
}
