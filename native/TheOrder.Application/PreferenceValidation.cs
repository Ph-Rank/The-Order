using TheOrder.Application;
using System.Text;

namespace TheOrder.Application;

public static class PreferenceValidation
{
    public static readonly string[] Sorts = ["added", "recent", "name", "rating", "playtime"];
    public static readonly string[] Views = ["all", "local", "wishlist", "unmatched", "unconfigured"];
    public static readonly string[] Statuses = ["all", .. TheOrder.Domain.GameProgress.Ordered];
    public static void Validate(ClientPreferences preferences)
    {
        if (!Enum.IsDefined(preferences.Theme) || preferences.Layout is not ("grid" or "list") || !Sorts.Contains(preferences.Sort) || preferences.Filters.Length > 50) throw new InvalidDataException(UiText.Get("界面偏好格式不正确。"));
        var ids = new HashSet<string>(StringComparer.Ordinal); var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var saved in preferences.Filters)
        {
            if (saved is null || string.IsNullOrWhiteSpace(saved.Name) || saved.Name.Length > 80 || saved.Name.Any(char.IsControl) || string.IsNullOrEmpty(saved.Id) || saved.Id.Length > 80 || !ids.Add(saved.Id)
                || !names.Add(saved.Name.Normalize(NormalizationForm.FormKC).ToLowerInvariant())) throw new InvalidDataException(UiText.Get("常用筛选名称无效或重复。"));
            var q = saved.Query;
            if (q is null || !Views.Contains(q.View) || !Statuses.Contains(q.Status) || !Sorts.Contains(q.Sort) || q.Text is null || q.Text.Length > 1000 || q.Category is null || q.Category.Length > 200 || q.Tag is null || q.Tag.Length > 200 || q.MinRating is null
                || q.MinRating.Length > 10 || q.MinRating.Length > 0 && (!double.TryParse(q.MinRating, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rating) || !double.IsFinite(rating) || rating is < 0 or > 10)) throw new InvalidDataException(UiText.Get("常用筛选条件格式不正确。"));
        }
    }
}
