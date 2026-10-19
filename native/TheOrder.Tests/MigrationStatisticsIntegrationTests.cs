using System.Text;
using System.Text.Json;
using TheOrder.Application;
using TheOrder.Application.Migrations;
using TheOrder.Infrastructure.Legacy;
using TheOrder.Infrastructure.Migration;
using TheOrder.Platform.Windows;
using Xunit;

namespace TheOrder.Tests;

public sealed class MigrationStatisticsIntegrationTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(60, 3600, 1)]
    public async Task ImportedHistorySurvivesRestartWithoutFabricatingDashboardActivity(int dailyMinutes, long datedSeconds, int activeDays)
    {
        var source = JsonSerializer.SerializeToUtf8Bytes(new[] { new {
            Uuid = "88888888-1111-4111-8111-111111111111", Name = new { Value = "合成迁移统计样本" },
            Ids = new string?[] { "123", "456" }, TotalPlayTime = 120, PlayType = 1, MyRate = 0,
            LastPlayTime = "2026-10-18T08:00:00Z",
            PlayedTime = new Dictionary<string, int> { ["2026/10/18"] = dailyMinutes }
        } });
        var data = await new PotatoVnImportAdapter().ReadAsync(new MemoryStream(source), ImportHash.Bytes(source));
        var root = Path.Combine(Path.GetTempPath(), "the-order-migration-stats-" + Guid.NewGuid().ToString("N"));
        var codec = new NativeLibraryCodec(); MigrationTestTarget.Create(root, codec);
        try
        {
            var plan = MigrationTestTarget.PreviewPlan(root, data, codec);
            await MigrationTestTarget.ApplyAsync(root, data, plan.Id, codec);
            var reopened = MigrationTestTarget.Preview(root, codec);
            var game = Assert.Single(reopened.Entries);
            Assert.Empty(game.Sessions); Assert.Equal(0, game.PlayCount);
            foreach (var period in Enum.GetValues<StatisticsPeriod>())
            {
                var dashboard = StatisticsDashboardBuilder.Build(new(reopened.Entries, period, new(2026, 10, 19), TimeZoneInfo.Utc));
                Assert.Equal(7200, dashboard.LifetimeSeconds); Assert.Equal(7200 - datedSeconds, dashboard.UnknownSeconds);
                Assert.Equal(datedSeconds, dashboard.PeriodSeconds); Assert.Equal(datedSeconds, dashboard.Trend.Sum(x => x.Seconds));
                Assert.Equal(activeDays, dashboard.ActiveDays); Assert.Equal(activeDays, dashboard.PlayedGames);
                Assert.Equal(0, dashboard.CompletedGames);
            }
            var path = Path.Combine(root, "library.json"); var before = ImportHash.Bytes(File.ReadAllBytes(path));
            var repeat = MigrationTestTarget.PreviewPlan(root, data, codec);
            var receipt = (await MigrationTestTarget.ApplyAsync(root, data, repeat.Id, codec)).Value;
            Assert.Equal(0, receipt.Created); Assert.Equal(0, receipt.Filled);
            Assert.Equal(before, ImportHash.Bytes(File.ReadAllBytes(path)));
        }
        finally
        {
            var absolute = Path.GetFullPath(root); var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (!absolute.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(absolute).StartsWith("the-order-migration-stats-", StringComparison.Ordinal)) throw new InvalidOperationException();
            Directory.Delete(absolute, true);
        }
    }
}
