using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    /// <summary>
    /// Quality comparisons now stay within a media type, so one profile can want an ebook and an
    /// audiobook of the same book without either blocking the other. New installs get an
    /// "eBook + Audiobook" profile from <c>QualityProfileService</c>; an existing install whose
    /// profiles are all single-type gets the same one here, so authors can be moved onto it.
    /// Nothing is reassigned - the user picks which authors should want both.
    /// </summary>
    [Migration(044)]
    public class ebook_and_audiobook_quality_profile : NzbDroneMigrationBase
    {
        private const string ProfileName = "eBook + Audiobook";
        private const int FirstAudioQualityId = 10;
        private const int CutoffQualityId = 12;

        // Stored order of the default quality definitions: text qualities, then audio.
        private static readonly int[] QualityOrder = { 0, 1, 2, 3, 4, 13, 10, 12, 11 };
        private static readonly int[] Allowed = { 2, 3, 4, 10, 12 };

        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(AddProfile);
        }

        private void AddProfile(IDbConnection conn, IDbTransaction tran)
        {
            var rows = conn.Query<QualityProfile044>("SELECT \"Id\", \"Name\", \"Items\" FROM \"QualityProfiles\"", transaction: tran).ToList();

            // A fresh database has no profiles yet; the service seeds all three on first start.
            if (!rows.Any())
            {
                return;
            }

            if (rows.Any(x => x.Name == ProfileName) || rows.Any(x => AllowsBothMediaTypes(x.Items)))
            {
                return;
            }

            var items = QualityOrder.Select(id => new { quality = id, items = new List<object>(), allowed = Allowed.Contains(id) });

            conn.Execute(
                "INSERT INTO \"QualityProfiles\" (\"Name\", \"Cutoff\", \"Items\", \"UpgradeAllowed\", \"FormatItems\", \"MinFormatScore\", \"CutoffFormatScore\") " +
                "VALUES (@Name, @Cutoff, @Items, 1, '[]', 0, 0)",
                new
                {
                    Name = ProfileName,
                    Cutoff = CutoffQualityId,
                    Items = JsonSerializer.Serialize(items)
                },
                transaction: tran);
        }

        private static bool AllowsBothMediaTypes(string itemsJson)
        {
            if (string.IsNullOrWhiteSpace(itemsJson))
            {
                return false;
            }

            var allowed = new List<int>();

            using (var doc = JsonDocument.Parse(itemsJson))
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    CollectAllowed(item, allowed);
                }
            }

            return allowed.Any(id => id < FirstAudioQualityId) && allowed.Any(id => id >= FirstAudioQualityId);
        }

        private static void CollectAllowed(JsonElement item, List<int> allowed)
        {
            var isAllowed = item.TryGetProperty("allowed", out var allowedElement) && allowedElement.GetBoolean();

            if (isAllowed && item.TryGetProperty("quality", out var quality) && quality.ValueKind == JsonValueKind.Number)
            {
                allowed.Add(quality.GetInt32());
            }

            if (isAllowed && item.TryGetProperty("items", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                {
                    CollectAllowed(child, allowed);
                }
            }
        }

        private class QualityProfile044
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Items { get; set; }
        }
    }
}
