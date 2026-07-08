namespace VideoInferenceDemo;

public static class SessionStatusTextFormatter
{
    public static string GetRunStateText(SessionStatusSnapshot snapshot)
    {
        return snapshot.RunState switch
        {
            SessionRunState.Paused => "已暂停",
            SessionRunState.Disabled => "已禁用",
            SessionRunState.Starting => "启动中",
            SessionRunState.Running => "运行中",
            SessionRunState.Stopped => snapshot.IsVideoSource ? "待播放" : "已停止",
            SessionRunState.Completed => "已完成",
            SessionRunState.Blocked => "已阻塞",
            SessionRunState.Error => "异常",
            SessionRunState.NoModel => "无模型",
            SessionRunState.NoSource => "无输入",
            SessionRunState.ModelSelected => "模型已选",
            _ => "空闲"
        };
    }

    public static string GetTransitionStatusText(SessionStatusSnapshot snapshot)
    {
        return snapshot.TransitionState switch
        {
            SessionTransitionState.Normal => "流转正常",
            SessionTransitionState.Abnormal => "流转异常",
            _ => "未触发"
        };
    }

    public static string GetStatusBadgeText(SessionStatusSnapshot snapshot)
    {
        return snapshot.BadgeState switch
        {
            SessionBadgeState.Error => "异常",
            SessionBadgeState.Paused => "已暂停",
            SessionBadgeState.Disabled => "已禁用",
            SessionBadgeState.Running => "运行中",
            SessionBadgeState.Pending => "待处理",
            _ => "空闲"
        };
    }
}
