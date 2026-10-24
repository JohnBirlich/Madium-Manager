namespace MadiumManager.Desktop.Models;

public enum SessionState
{
    Stopped,
    Checking,
    Starting,
    Running,
    Reconnecting,
    Stopping,
    Faulted
}
