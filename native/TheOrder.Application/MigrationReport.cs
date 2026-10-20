using System.Text;
using System.Text.Json;

namespace TheOrder.Application.Migrations;

public static class MigrationReport
{
    public static string Json(MigrationPlan plan, int targetCount, MigrationCommit? committed = null, MigrationVerification? verification = null)
    {
        var d = plan.Dataset; var rows = plan.Rows;
        var allIssues = d.Issues.Concat(d.Games.SelectMany(g => g.Issues)).ToArray();
        return JsonSerializer.Serialize(new
        {
            schema = 3, planId = plan.Id, source = new { manager = d.Manager, format = d.Format, version = d.Format, documentDigest = d.DocumentDigest },
            counts = new { raw = d.RawCount, normalized = d.Games.Length, transferable = rows.Count(x => x.Action is ImportAction.Create or ImportAction.FillMissing),
                confirmed = rows.Count(x => x.Match == ImportMatch.Confirmed), needsConfirmation = rows.Count(x => x.Match == ImportMatch.Candidate),
                conflicts = rows.Count(x => x.Match == ImportMatch.Conflict), excludedOtherOrUnverifiedType = d.Issues.Count(i => i.Code == "NON_GALGAME_OR_UNVERIFIED_IDENTITY_EXCLUDED"),
                invalidRecords = d.RawCount - d.Games.Length - d.Issues.Count(i => i.Code == "NON_GALGAME_OR_UNVERIFIED_IDENTITY_EXCLUDED"),
                alreadyImported = rows.Count(x => x.Action == ImportAction.AlreadyImported), skipped = rows.Count(x => x.Action is ImportAction.Skip or ImportAction.AlreadyImported) + d.RawCount - d.Games.Length },
            accounting = new { totalSourceSeconds = d.Games.Sum(g => g.TotalSeconds), datedAggregateSeconds = d.Games.SelectMany(g => g.Days).Sum(x => x.Seconds),
                reliableSessionSeconds = d.Games.SelectMany(g => g.Sessions).Sum(x => x.Seconds),
                undatedSourceSeconds = d.Games.Sum(g => g.TotalSeconds - g.Days.Sum(x => x.Seconds) - g.Sessions.Sum(x => x.Seconds)),
                sourceUnit = d.Manager is "potatovn" or "reinamanager" ? "minutes converted to seconds" : d.Manager == "vnite" ? "milliseconds floored to whole seconds" : "seconds",
                dailyPrecision = "source calendar date; no invented start/end or launch count",
                sourceTimeZone = d.SourceTimeZone ?? "unspecified", localTimestamps = "explicit offset or explicitly selected source zone; otherwise source-local evidence only" },
            paths = new { declarations = d.Games.Sum(g => g.Installations.Length), probed = 0, existence = "not verified; no game directory accessed", unsafeOrUnresolved = allIssues.Count(i => i.Code is "INVALID_OR_UNRESOLVED_INSTALLATION_PATH" or "UNSAFE_EXECUTABLE_NOT_IMPORTED") },
            target = new { testOnly = true, identityScope = plan.TargetScope, beforeCount = targetCount, projectedCount = targetCount + rows.Count(r => r.Action == ImportAction.Create), actualCommit = committed, verification },
            rows = rows.Select(r => new { r.RecordKey, match = r.Match.ToString(), action = r.Action.ToString(), r.TargetKey, r.Reasons,
                r.CandidateKeys, fields = r.Match == ImportMatch.Conflict ? [] : r.Fields, blockedFields = r.Match == ImportMatch.Conflict ? r.Fields : [],
                r.HistoryAdditionalSeconds, r.HistoryNewlyDatedSeconds, r.HistoryDeduplicatedSeconds, r.HistoryWarnings }),
            issues = allIssues, omitted = new[] { "credentials", "cookies", "save files", "game files", "scripts", "raw launch arguments", "plugin configuration", "image bytes" },
            gates = new { sourceKnownFormat = true, conversionImplemented = true, isolatedImport = committed is not null, realUserMigration = "NOT_VERIFIED", winUiWizard = "IMPLEMENTED_SEPARATE_VALIDATION" },
            privacy = "record keys are hashed; titles, notes, absolute paths and personal field values are omitted"
        }, new JsonSerializerOptions { WriteIndented = true });
    }
    public static string Markdown(MigrationPlan plan, int targetCount, MigrationCommit? committed = null, MigrationVerification? verification = null)
    {
        using var json = JsonDocument.Parse(Json(plan, targetCount, committed)); var root = json.RootElement; var counts = root.GetProperty("counts");
        var b = new StringBuilder("# Library migration preview\n\n");
        b.AppendLine($"Source: {plan.Dataset.Manager}; layout: {plan.Dataset.Format}. Plan: `{plan.Id}`.");
        b.AppendLine("\n| Measure | Count |\n| --- | ---: |");
        foreach (var p in counts.EnumerateObject()) b.AppendLine($"| {p.Name} | {p.Value} |");
        b.AppendLine("\n| Record key | Match | Action | Reasons |\n| --- | --- | --- | --- |");
        foreach (var r in plan.Rows) b.AppendLine($"| {r.RecordKey} | {r.Match} | {r.Action} | {string.Join(", ", r.Reasons.Concat(r.HistoryWarnings))} |");
        b.AppendLine($"\nReviewed time addition: {plan.Rows.Sum(r => r.HistoryAdditionalSeconds)} seconds; newly dated: {plan.Rows.Sum(r => r.HistoryNewlyDatedSeconds)} seconds; exact duplicate sessions excluded: {plan.Rows.Sum(r => r.HistoryDeduplicatedSeconds)} seconds.");
        var issues = plan.Dataset.Issues.Concat(plan.Dataset.Games.SelectMany(g => g.Issues)).GroupBy(i => (i.Field, i.Code));
        b.AppendLine("\n| Unmapped / validation field | Reason | Count |\n| --- | --- | ---: |");
        foreach (var issue in issues) b.AppendLine($"| {issue.Key.Field} | {issue.Key.Code} | {issue.Count()} |");
        var accounting = root.GetProperty("accounting");
        b.AppendLine($"\nSource seconds: {accounting.GetProperty("totalSourceSeconds")}; dated aggregates: {accounting.GetProperty("datedAggregateSeconds")}; reliable sessions: {accounting.GetProperty("reliableSessionSeconds")}; undated: {accounting.GetProperty("undatedSourceSeconds")}. Target entries: {targetCount} → {targetCount + plan.Rows.Count(r => r.Action == ImportAction.Create)}.");
        b.AppendLine("\nUnits are converted from the pinned source format to whole seconds. Daily aggregates retain dates, never invented sessions; undated totals stay undated. Reviewed addition records the accounted contribution to avoid duplicate time. Dates without an offset are retained as local source evidence.");
        b.AppendLine("\nPaths are syntax-checked only. No game directory, platform account or remote URL is accessed. Reports omit names, notes, absolute paths and credentials.");
        b.AppendLine(committed is null ? "\nPreview only; no target writes." : $"\nExplicit Test commit: {committed.Created} created, {committed.Filled} filled, {committed.Skipped} skipped.");
        if (verification is not null) b.AppendLine($"\nReopen verification: {verification.Status}; entries {verification.BeforeCount} → {verification.AfterCount} (expected {verification.ExpectedCount}); effective seconds {verification.BeforeSeconds} → {verification.AfterSeconds}.");
        b.AppendLine("\nReal user migration: NOT VERIFIED. WinUI wizard: implemented, validated separately from this CLI run."); return b.ToString();
    }
}
