using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DuxiuLedger.WinUI;

public sealed class NotificationService
{
    private bool _registered;

    public bool Register()
    {
        try { AppNotificationManager.Default.Register(); _registered = true; return true; }
        catch { return false; }
    }

    public void Show(string title, string message)
    {
        if (!_registered && !Register()) throw new InvalidOperationException("Windows 通知服务注册失败。 ");
        var notification = new AppNotificationBuilder().AddText(title).AddText(message).BuildNotification();
        AppNotificationManager.Default.Show(notification);
    }

    public void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
        _registered = false;
    }
}
