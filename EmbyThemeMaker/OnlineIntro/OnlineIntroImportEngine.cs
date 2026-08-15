using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EmbyThemeMaker.Config;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace EmbyThemeMaker.OnlineIntro
{
    internal sealed class OnlineIntroImportEngine
    {
        private const int HttpTimeoutSeconds = 15;
        private const int MinimumDelayMilliseconds = 400;
        private const int MaximumLookupsPerRun = 400;
        private const int MaximumEpisodesPerSeries = 20;
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;

        public OnlineIntroImportEngine(ILibraryManager libraryManager, IItemRepository itemRepository, ILogger logger)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = logger;
        }

        public OnlineIntroImportSummary Run(ThemeMakerOptions options, bool preview,
                                            IProgress<double> progress, CancellationToken cancellationToken)
        {
            var summary = new OnlineIntroImportSummary();
            var maxLookups = Math.Min(MaximumLookupsPerRun, Math.Max(1, options.OnlineIntroMaxLookups));
            var maxEpisodes = Math.Min(MaximumEpisodesPerSeries,
                Math.Max(1, options.OnlineIntroMaxEpisodesPerSeries));
            var delayMilliseconds = Math.Min(60000,
                Math.Max(MinimumDelayMilliseconds, options.OnlineIntroDelayMilliseconds));
            progress?.Report(0);
            var allSeries = GetAllSeries();
            var localIndex = BuildLocalIntroIndex(allSeries, options, cancellationToken);
            var targetSeries = GetTargetSeries(allSeries, options);
            summary.SeriesConsidered = targetSeries.Count;
            _logger.Info("[ThemeMaker] local and online intro {0} starting: series={1}, localSources={2}, "
                + "maxLookups={3}, episodesPerSeries={4}, delayMs={5}",
                preview ? "preview" : "apply", targetSeries.Count, localIndex.Values.Sum(sources => sources.Count),
                maxLookups, maxEpisodes, delayMilliseconds);

            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EmbyThemeMaker/1.0");
                for (var seriesIndex = 0; seriesIndex < targetSeries.Count; seriesIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ProcessSeries(targetSeries[seriesIndex], localIndex, options, maxLookups, maxEpisodes,
                        delayMilliseconds, preview, client, summary, cancellationToken);
                    progress?.Report(100.0 * (seriesIndex + 1) / Math.Max(1, targetSeries.Count));
                }
            }

            progress?.Report(100);
            LogSummary(preview, summary);
            return summary;
        }

        private List<Series> GetAllSeries()
        {
            try
            {
                return (_libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "Series" },
                    Recursive = true,
                }) ?? Array.Empty<BaseItem>())
                    .OfType<Series>()
                    .OrderBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                _logger.Error("[ThemeMaker] local and online intro import could not query series");
                return new List<Series>();
            }
        }

        private static List<Series> GetTargetSeries(IEnumerable<Series> allSeries, ThemeMakerOptions options)
        {
            var result = string.IsNullOrWhiteSpace(options.OnlyUnderPath)
                ? allSeries.ToList()
                : allSeries.Where(series => IsUnder(series.Path, options.OnlyUnderPath)).ToList();
            return options.Limit > 0 ? result.Take(options.Limit).ToList() : result;
        }

        private Dictionary<string, List<LocalIntroSource>> BuildLocalIntroIndex(IEnumerable<Series> allSeries,
                                                                                  ThemeMakerOptions options,
                                                                                  CancellationToken cancellationToken)
        {
            var index = new Dictionary<string, List<LocalIntroSource>>(StringComparer.Ordinal);
            foreach (var series in allSeries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var providers = IntroImportHelpers.SelectProviders(series.ProviderIds);
                if (providers.Count == 0)
                {
                    continue;
                }

                try
                {
                    foreach (var item in series.GetRecursiveChildren())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var episode = item as Episode;
                        if (episode == null || string.IsNullOrEmpty(episode.Path)
                            || episode.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                            || !episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue)
                        {
                            continue;
                        }

                        List<ChapterInfo> chapters;
                        if (!TryGetChapters(episode, cancellationToken, out chapters))
                        {
                            continue;
                        }

                        IntroMarkerRange range;
                        if (!IntroImportHelpers.TryExtractMarkerRange(chapters, options.MinIntro, options.MaxIntro,
                                                                       episode.RunTimeTicks, out range))
                        {
                            continue;
                        }

                        foreach (var provider in providers)
                        {
                            var key = IntroImportHelpers.BuildEpisodeKey(provider,
                                episode.ParentIndexNumber.Value, episode.IndexNumber.Value);
                            List<LocalIntroSource> sources;
                            if (!index.TryGetValue(key, out sources))
                            {
                                sources = new List<LocalIntroSource>();
                                index.Add(key, sources);
                            }

                            sources.Add(new LocalIntroSource
                            {
                                EpisodeInternalId = episode.InternalId,
                                Range = range,
                                RuntimeTicks = episode.RunTimeTicks,
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    _logger.Error("[ThemeMaker] local intro index could not enumerate episodes for '{0}'",
                        series.Name ?? "?");
                }
            }

            foreach (var sources in index.Values)
            {
                sources.Sort((left, right) =>
                {
                    var byStart = left.Range.StartTicks.CompareTo(right.Range.StartTicks);
                    if (byStart != 0)
                    {
                        return byStart;
                    }

                    var byEnd = left.Range.EndTicks.CompareTo(right.Range.EndTicks);
                    return byEnd != 0 ? byEnd : left.EpisodeInternalId.CompareTo(right.EpisodeInternalId);
                });
            }

            return index;
        }

        private void ProcessSeries(Series series, Dictionary<string, List<LocalIntroSource>> localIndex,
                                   ThemeMakerOptions options, int maxLookups, int maxEpisodes,
                                   int delayMilliseconds, bool preview, HttpClient client,
                                   OnlineIntroImportSummary summary, CancellationToken cancellationToken)
        {
            var providers = IntroImportHelpers.SelectProviders(series.ProviderIds);
            if (providers.Count == 0)
            {
                summary.SkippedNoProvider++;
                return;
            }

            var candidates = GetEligibleEpisodes(series, summary, cancellationToken);
            foreach (var episode in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntroMarkerRange localRange;
                var localResult = FindLocalRange(localIndex, providers, episode, summary, out localRange);
                if (localResult == LocalLookupResult.Conflict)
                {
                    LogEpisode("rejected conflicting local intros", series, episode);
                    continue;
                }
                if (localResult != LocalLookupResult.Match)
                {
                    continue;
                }

                summary.Candidates++;
                if (preview)
                {
                    summary.Previewed++;
                    summary.LocalPreviewed++;
                    LogEpisode("would copy local intro " + FormatRange(localRange), series, episode);
                    return;
                }

                var applyResult = TryApplyMarkers(episode, localRange, summary, cancellationToken);
                if (applyResult == ApplyResult.Applied)
                {
                    summary.Applied++;
                    summary.LocalApplied++;
                    LogEpisode("copied local intro " + FormatRange(localRange), series, episode);
                    return;
                }
                if (applyResult == ApplyResult.Existing)
                {
                    LogEpisode("kept existing intro markers", series, episode);
                    return;
                }
            }

            if (summary.OnlineStopped)
            {
                return;
            }

            var provider = providers[0];
            foreach (var episode in candidates.Take(maxEpisodes))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (summary.Lookups >= maxLookups)
                {
                    summary.LookupLimitReached = true;
                    return;
                }
                if (summary.Lookups > 0)
                {
                    Task.Delay(delayMilliseconds, cancellationToken).GetAwaiter().GetResult();
                }

                summary.Lookups++;
                IntroSegment segment;
                bool hasIntro;
                var requestStatus = FetchIntro(client, provider, episode, cancellationToken, out segment, out hasIntro);
                if (requestStatus == RequestStatus.RateLimited)
                {
                    summary.RateLimited++;
                    summary.OnlineStopped = true;
                    LogEpisode("online intro rate limit reached; stopping online lookups", series, episode);
                    return;
                }
                if (requestStatus == RequestStatus.Error)
                {
                    summary.Errors++;
                    LogEpisode("online intro request failed", series, episode);
                    continue;
                }
                if (requestStatus == RequestStatus.Miss || !hasIntro)
                {
                    summary.Misses++;
                    LogEpisode("online intro miss", series, episode);
                    continue;
                }

                IntroMarkerRange range;
                if (!IntroImportHelpers.TryValidate(segment, options.MinIntro, options.MaxIntro,
                                                    episode.RunTimeTicks, out range))
                {
                    summary.Rejected++;
                    LogEpisode("rejected online intro response", series, episode);
                    continue;
                }

                summary.Candidates++;
                if (preview)
                {
                    summary.Previewed++;
                    LogEpisode("would import online intro " + FormatRange(range), series, episode);
                    return;
                }

                var onlineApplyResult = TryApplyMarkers(episode, range, summary, cancellationToken);
                if (onlineApplyResult == ApplyResult.Applied)
                {
                    summary.Applied++;
                    LogEpisode("imported online intro " + FormatRange(range), series, episode);
                    return;
                }
                if (onlineApplyResult == ApplyResult.Existing)
                {
                    LogEpisode("kept existing intro markers", series, episode);
                    return;
                }
            }
        }

        private LocalLookupResult FindLocalRange(Dictionary<string, List<LocalIntroSource>> localIndex,
                                                  IEnumerable<ProviderSelection> providers, Episode episode,
                                                  OnlineIntroImportSummary summary, out IntroMarkerRange range)
        {
            range = null;
            var uniqueSources = new Dictionary<long, LocalIntroSource>();
            foreach (var provider in providers)
            {
                var key = IntroImportHelpers.BuildEpisodeKey(provider, episode.ParentIndexNumber.Value,
                    episode.IndexNumber.Value);
                List<LocalIntroSource> sources;
                if (key == null || !localIndex.TryGetValue(key, out sources))
                {
                    continue;
                }
                foreach (var source in sources)
                {
                    uniqueSources[source.EpisodeInternalId] = source;
                }
            }

            var compatible = uniqueSources.Values.Where(source =>
                IntroImportHelpers.AreRuntimesCompatible(episode.RunTimeTicks, source.RuntimeTicks)
                && (!episode.RunTimeTicks.HasValue || episode.RunTimeTicks.Value <= 0
                    || source.Range.EndTicks <= episode.RunTimeTicks.Value)).ToList();
            if (compatible.Count == 0)
            {
                return LocalLookupResult.None;
            }

            summary.LocalMatches++;
            if (IntroImportHelpers.HasMateriallyConflictingRanges(compatible.Select(source => source.Range)))
            {
                summary.LocalConflicts++;
                return LocalLookupResult.Conflict;
            }

            range = compatible[0].Range;
            return LocalLookupResult.Match;
        }

        private List<Episode> GetEligibleEpisodes(Series series, OnlineIntroImportSummary summary,
                                                   CancellationToken cancellationToken)
        {
            var episodes = new List<Episode>();
            try
            {
                foreach (var item in series.GetRecursiveChildren())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var episode = item as Episode;
                    if (episode == null || string.IsNullOrEmpty(episode.Path)
                        || !episode.Path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)
                        || !episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue)
                    {
                        summary.SkippedIneligible++;
                        continue;
                    }

                    List<ChapterInfo> chapters;
                    if (!TryGetChapters(episode, cancellationToken, out chapters))
                    {
                        summary.Errors++;
                        continue;
                    }
                    if (IntroImportHelpers.HasAnyIntroMarker(chapters))
                    {
                        summary.SkippedExisting++;
                        continue;
                    }
                    episodes.Add(episode);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                _logger.Error("[ThemeMaker] local and online intro import could not enumerate episodes for '{0}'",
                    series.Name ?? "?");
            }

            return episodes
                .OrderBy(episode => episode.ParentIndexNumber.Value)
                .ThenBy(episode => episode.IndexNumber.Value)
                .ThenBy(episode => episode.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private RequestStatus FetchIntro(HttpClient client, ProviderSelection provider, Episode episode,
                                         CancellationToken cancellationToken, out IntroSegment segment, out bool hasIntro)
        {
            segment = null;
            hasIntro = false;
            try
            {
                var url = IntroImportHelpers.BuildRequestUrl(provider, episode.ParentIndexNumber.Value,
                    episode.IndexNumber.Value, episode.RunTimeTicks);
                using (var response = client.GetAsync(url, cancellationToken).GetAwaiter().GetResult())
                {
                    if ((int)response.StatusCode == 404)
                    {
                        return RequestStatus.Miss;
                    }

                    if ((int)response.StatusCode == 429)
                    {
                        return RequestStatus.RateLimited;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return RequestStatus.Error;
                    }

                    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return IntroImportHelpers.TryParseResponse(json, out segment, out hasIntro)
                        ? RequestStatus.Success
                        : RequestStatus.Error;
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return RequestStatus.Error;
            }
            catch (Exception)
            {
                return RequestStatus.Error;
            }
        }

        private ApplyResult TryApplyMarkers(Episode episode, IntroMarkerRange range,
                                            OnlineIntroImportSummary summary,
                                            CancellationToken cancellationToken)
        {
            List<ChapterInfo> currentChapters;
            if (!TryGetChapters(episode, cancellationToken, out currentChapters))
            {
                summary.Errors++;
                return ApplyResult.Error;
            }

            if (IntroImportHelpers.HasAnyIntroMarker(currentChapters))
            {
                summary.SkippedExisting++;
                return ApplyResult.Existing;
            }

            try
            {
                var merged = IntroImportHelpers.MergeChapters(currentChapters, range);
                _itemRepository.SaveChapters(episode.InternalId, merged);
                return ApplyResult.Applied;
            }
            catch (Exception)
            {
                summary.Errors++;
                return ApplyResult.Error;
            }
        }

        private bool TryGetChapters(Episode episode, CancellationToken cancellationToken, out List<ChapterInfo> chapters)
        {
            try
            {
                chapters = _itemRepository.GetChapters(episode, cancellationToken) ?? new List<ChapterInfo>();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                chapters = null;
                return false;
            }
        }

        private void LogEpisode(string outcome, Series series, Episode episode)
        {
            _logger.Info("[ThemeMaker] intro marker import {0}: {1} S{2}E{3}", outcome,
                series.Name ?? "?", episode.ParentIndexNumber.Value, episode.IndexNumber.Value);
        }

        private void LogSummary(bool preview, OnlineIntroImportSummary summary)
        {
            _logger.Info("[ThemeMaker] local and online intro {0} summary: series={1}, lookups={2}, candidates={3}, "
                + "localMatches={4}, localConflicts={5}, previewed={6}, localPreviewed={7}, applied={8}, "
                + "localApplied={9}, misses={10}, rejected={11}, existing={12}, noProvider={13}, "
                + "ineligible={14}, errors={15}, rateLimited={16}{17}",
                preview ? "preview" : "apply", summary.SeriesConsidered, summary.Lookups, summary.Candidates,
                summary.LocalMatches, summary.LocalConflicts, summary.Previewed, summary.LocalPreviewed,
                summary.Applied, summary.LocalApplied, summary.Misses, summary.Rejected, summary.SkippedExisting,
                summary.SkippedNoProvider, summary.SkippedIneligible, summary.Errors, summary.RateLimited,
                summary.LookupLimitReached ? ", lookupLimitReached=true" : string.Empty);
        }

        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            try
            {
                var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
                    || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string FormatRange(IntroMarkerRange range)
        {
            return range.StartSeconds.ToString("0.###") + "-" + range.EndSeconds.ToString("0.###") + "s";
        }

        private enum RequestStatus
        {
            Success,
            Miss,
            RateLimited,
            Error,
        }

        private enum LocalLookupResult
        {
            None,
            Match,
            Conflict,
        }

        private enum ApplyResult
        {
            Applied,
            Existing,
            Error,
        }
    }

    internal sealed class LocalIntroSource
    {
        public long EpisodeInternalId { get; set; }
        public IntroMarkerRange Range { get; set; }
        public long? RuntimeTicks { get; set; }
    }

    internal sealed class OnlineIntroImportSummary
    {
        public int SeriesConsidered { get; set; }
        public int Lookups { get; set; }
        public int Candidates { get; set; }
        public int LocalMatches { get; set; }
        public int LocalConflicts { get; set; }
        public int Previewed { get; set; }
        public int LocalPreviewed { get; set; }
        public int Applied { get; set; }
        public int LocalApplied { get; set; }
        public int Misses { get; set; }
        public int Rejected { get; set; }
        public int SkippedExisting { get; set; }
        public int SkippedNoProvider { get; set; }
        public int SkippedIneligible { get; set; }
        public int Errors { get; set; }
        public int RateLimited { get; set; }
        public bool LookupLimitReached { get; set; }
        public bool OnlineStopped { get; set; }
    }
}
