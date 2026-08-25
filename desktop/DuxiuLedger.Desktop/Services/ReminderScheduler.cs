using System.Diagnostics;
using DuxiuLedger.Desktop.Models;

namespace DuxiuLedger.Desktop.Services;

public static class ReminderScheduler
{
    private const string DailyTask = "DuxiuLedger-DailyReminder";
    private const string WeeklyTask = "DuxiuLedger-WeeklySummary";

    public static void Update(AppSettings settings)
    {
        Configure(DailyTask, settings.DailyReminderEnabled, "DAILY", settings.DailyReminderTime, null, "--daily-reminder");
        Configure(WeeklyTask, settings.WeeklySummaryEnabled, "WEEKLY", settings.WeeklySummaryTime, ToScheduleDay(settings.WeeklySummaryDay), "--weekly-summary");
    }

    private static void Configure(string taskName, bool enabled, string schedule, string time, string? day, string argument)
    {
        if (!enabled)
        {
            Run(["/Delete", "/TN", taskName, "/F"], ignoreFailure: true);
            return;
        }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定应用程序路径。 ");
        var taskRun = $"\"{executable}\" {argument}";
        var args = new List<string> { "/Create", "/TN", taskName, "/TR", taskRun, "/SC", schedule, "/ST", time, "/F" };
        if (day is not null) { args.Add("/D"); args.Add(day); }
        Run(args, ignoreFailure: false);
    }

    private static void Run(IEnumerable<string> arguments, bool ignoreFailure)
    {
        var start = new ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Windows 任务计划程序。 ");
        process.WaitForExit(10000);
        if (!process.HasExited) { process.Kill(); throw new TimeoutException("更新提醒任务超时。 "); }
        if (process.ExitCode != 0 && !ignoreFailure) throw new InvalidOperationException("Windows 提醒任务创建失败，请检查任务计划程序是否可用。 ");
    }

    private static string ToScheduleDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MON", DayOfWeek.Tuesday => "TUE", DayOfWeek.Wednesday => "WED", DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI", DayOfWeek.Saturday => "SAT", _ => "SUN"
    };
}
