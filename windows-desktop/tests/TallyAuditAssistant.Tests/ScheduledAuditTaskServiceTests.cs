using TallyAuditAssistant.App.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class ScheduledAuditTaskServiceTests
{
    private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void BuildFrequencyArguments_ReturnsDailyRecurrence()
    {
        Assert.Equal(new[] { "/SC", "DAILY" }, ScheduledAuditTaskService.BuildFrequencyArguments("Daily"));
    }

    [Fact]
    public void BuildFrequencyArguments_ReturnsMondayRecurrence()
    {
        Assert.Equal(
            new[] { "/SC", "WEEKLY", "/D", "MON" },
            ScheduledAuditTaskService.BuildFrequencyArguments("Weekly (Monday)"));
    }

    [Fact]
    public void BuildFrequencyArguments_ReturnsFirstOfMonthRecurrence()
    {
        Assert.Equal(
            new[] { "/SC", "MONTHLY", "/D", "1" },
            ScheduledAuditTaskService.BuildFrequencyArguments("Monthly (1st day)"));
    }

    [Fact]
    public void BuildFrequencyArguments_IsCaseInsensitive()
    {
        Assert.Equal(
            new[] { "/SC", "MONTHLY", "/D", "1" },
            ScheduledAuditTaskService.BuildFrequencyArguments("monthly (1st day)"));
    }

    [Fact]
    public void BuildFrequencyArguments_RejectsUnsupportedFrequencyInsteadOfSilentlyUsingDaily()
    {
        var error = Assert.Throws<ArgumentException>(
            () => ScheduledAuditTaskService.BuildFrequencyArguments("Quarterly"));

        Assert.Contains("Monthly (1st day)", error.Message);
    }

    [Fact]
    public void ParseScheduleDefinition_RecognizesMonthlyTaskAndTime()
    {
        var xml = $"""
            <Task xmlns="{TaskNamespace}">
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>2026-10-01T02:30:00</StartBoundary>
                  <ScheduleByMonth>
                    <DaysOfMonth><Day>1</Day></DaysOfMonth>
                    <Months><January/><February/><March/><April/><May/><June/><July/><August/><September/><October/><November/><December/></Months>
                  </ScheduleByMonth>
                </CalendarTrigger>
              </Triggers>
            </Task>
            """;

        var schedule = ScheduledAuditTaskService.ParseScheduleDefinition(xml);

        Assert.Equal("Monthly (1st day)", schedule.Frequency);
        Assert.Equal("02:30", schedule.Time);
    }

    [Fact]
    public void ParseScheduleDefinition_RecognizesWeeklyTask()
    {
        var xml = $"""
            <Task xmlns="{TaskNamespace}">
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>2026-10-05T07:15:00</StartBoundary>
                  <ScheduleByWeek>
                    <DaysOfWeek><Monday /></DaysOfWeek>
                    <WeeksInterval>1</WeeksInterval>
                  </ScheduleByWeek>
                </CalendarTrigger>
              </Triggers>
            </Task>
            """;

        var schedule = ScheduledAuditTaskService.ParseScheduleDefinition(xml);

        Assert.Equal("Weekly (Monday)", schedule.Frequency);
        Assert.Equal("07:15", schedule.Time);
    }

    [Fact]
    public void ParseScheduleDefinition_RecognizesDailyTask()
    {
        var xml = $"""
            <Task xmlns="{TaskNamespace}">
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>2026-10-10T09:00:00</StartBoundary>
                  <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                </CalendarTrigger>
              </Triggers>
            </Task>
            """;

        var schedule = ScheduledAuditTaskService.ParseScheduleDefinition(xml);

        Assert.Equal("Daily", schedule.Frequency);
        Assert.Equal("09:00", schedule.Time);
    }
}
