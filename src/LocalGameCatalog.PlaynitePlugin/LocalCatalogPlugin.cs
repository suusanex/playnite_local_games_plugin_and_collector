using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using LocalGameCatalog.Core;
using Playnite;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace LocalGameCatalog.PlaynitePlugin
{
    public sealed class LocalCatalogPlugin : LibraryPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const string ProductLinkName = "Product page";
        private readonly LocalCatalogSettingsViewModel settings;

        public LocalCatalogPlugin(IPlayniteAPI api)
            : base(api)
        {
            settings = new LocalCatalogSettingsViewModel(this);
            Properties = new LibraryPluginProperties
            {
                HasSettings = true,
                HasCustomizedGameImport = true
            };
        }

        public override Guid Id { get; } = new Guid("5f0c2a7e-1b44-4c9a-8e3d-7a6b9c0d1e2f");

        public override string Name => "Local Game Catalog";

        public override string LibraryIcon
        {
            get
            {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var directory = Path.GetDirectoryName(assemblyPath) ?? "";
                return Path.Combine(directory, "icon.png");
            }
        }

        public override ISettings GetSettings(bool firstRunSettingsView)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettingsView)
        {
            return new LocalCatalogSettingsView();
        }

        public override IEnumerable<Game> ImportGames(LibraryImportGamesArgs args)
        {
            var catalogPath = settings.Settings.CatalogPath;
            if (string.IsNullOrWhiteSpace(catalogPath) || !File.Exists(catalogPath))
            {
                Logger.Error("Catalog path is not configured or missing: " + catalogPath);
                yield break;
            }

            IReadOnlyList<CatalogGame> records;
            try
            {
                records = CatalogJsonl.Read(catalogPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
            {
                Logger.Error(exception, "Failed to read the local game catalog.");
                yield break;
            }

            Logger.Info("Importing local catalog from " + catalogPath);
            var added = 0;
            var updated = 0;
            foreach (var record in records)
            {
                args.CancelToken.ThrowIfCancellationRequested();
                if (IsExcluded(record.Id))
                {
                    Logger.Info("Skipped excluded catalog game " + record.Id);
                    continue;
                }

                var existing = PlayniteApi.Database.Games.FirstOrDefault(game => game.PluginId == Id && game.GameId == record.Id);
                if (existing == null)
                {
                    var created = CreateGame(record);
                    PlayniteApi.Database.Games.Add(created);
                    ApplyCover(created, catalogPath, record.CoverImage);
                    PlayniteApi.Database.Games.Update(created);
                    added++;
                    yield return created;
                    continue;
                }

                ApplyCatalogFields(existing, record, catalogPath);
                PlayniteApi.Database.Games.Update(existing);
                updated++;
                yield return existing;
            }

            Logger.Info("Local catalog import finished. added=" + added.ToString(CultureInfo.InvariantCulture) + " updated=" + updated.ToString(CultureInfo.InvariantCulture));
        }

        private Game CreateGame(CatalogGame record)
        {
            var game = new Game(record.Name)
            {
                GameId = record.Id,
                PluginId = Id,
                SourceId = GetOrCreateSource(CatalogSources.DisplayName(record.Source)).Id
            };
            ApplyProductLink(game, record.ProductUrl);
            ApplyDeveloper(game, record.Developer);
            ApplyReleaseDate(game, record.ReleaseDate);
            return game;
        }

        private void ApplyCatalogFields(Game game, CatalogGame record, string catalogPath)
        {
            if (!string.Equals(game.Name, record.Name, StringComparison.Ordinal))
            {
                game.Name = record.Name;
            }

            var sourceId = GetOrCreateSource(CatalogSources.DisplayName(record.Source)).Id;
            if (game.SourceId != sourceId)
            {
                game.SourceId = sourceId;
            }

            ApplyProductLink(game, record.ProductUrl);
            ApplyDeveloper(game, record.Developer);
            ApplyReleaseDate(game, record.ReleaseDate);
            ApplyCover(game, catalogPath, record.CoverImage);
        }

        private static void ApplyProductLink(Game game, string productUrl)
        {
            if (game.Links == null)
            {
                game.Links = new ObservableCollection<Link>();
            }

            var link = game.Links.FirstOrDefault(item => item != null && item.Name == ProductLinkName);
            if (link == null)
            {
                game.Links.Add(new Link(ProductLinkName, productUrl));
                return;
            }

            if (!string.Equals(link.Url, productUrl, StringComparison.Ordinal))
            {
                link.Url = productUrl;
            }
        }

        private void ApplyDeveloper(Game game, string? developer)
        {
            if (string.IsNullOrWhiteSpace(developer))
            {
                return;
            }

            var company = PlayniteApi.Database.Companies.FirstOrDefault(item => item.Name == developer);
            if (company == null)
            {
                company = new Company(developer);
                PlayniteApi.Database.Companies.Add(company);
            }

            if (game.DeveloperIds != null && game.DeveloperIds.Count == 1 && game.DeveloperIds[0] == company.Id)
            {
                return;
            }

            game.DeveloperIds = new List<Guid> { company.Id };
        }

        private static void ApplyReleaseDate(Game game, string? releaseDate)
        {
            if (string.IsNullOrWhiteSpace(releaseDate))
            {
                return;
            }

            if (!DateTime.TryParseExact(releaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return;
            }

            var value = new ReleaseDate(parsed.Year, parsed.Month, parsed.Day);
            if (!Equals(game.ReleaseDate, value))
            {
                game.ReleaseDate = value;
            }
        }

        private void ApplyCover(Game game, string catalogPath, string? relativePath)
        {
            if (relativePath == null || relativePath.Trim().Length == 0)
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = CatalogPaths.ResolveCover(catalogPath, relativePath);
            }
            catch (InvalidDataException exception)
            {
                Logger.Error(exception, "Cover path is invalid for " + game.GameId);
                return;
            }

            if (!File.Exists(fullPath))
            {
                Logger.Warn("Cover file is missing for " + game.GameId + ": " + fullPath);
                return;
            }

            if (game.Id == Guid.Empty)
            {
                Logger.Error("Cannot store a cover before the game has an id: " + game.GameId);
                return;
            }

            if (!string.IsNullOrEmpty(game.CoverImage))
            {
                PlayniteApi.Database.RemoveFile(game.CoverImage);
            }

            game.CoverImage = PlayniteApi.Database.AddFile(fullPath, game.Id);
        }

        private bool IsExcluded(string gameId)
        {
            var exclusionId = ImportExclusionItem.GetId(gameId, Id);
            return PlayniteApi.Database.ImportExclusions.Get(exclusionId) != null;
        }

        private GameSource GetOrCreateSource(string name)
        {
            var source = PlayniteApi.Database.Sources.FirstOrDefault(item => item.Name == name);
            if (source != null)
            {
                return source;
            }

            source = new GameSource(name);
            PlayniteApi.Database.Sources.Add(source);
            return source;
        }
    }
}
