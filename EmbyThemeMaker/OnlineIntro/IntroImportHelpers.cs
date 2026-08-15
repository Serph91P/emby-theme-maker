using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using MediaBrowser.Model.Entities;

namespace EmbyThemeMaker.OnlineIntro
{
    public enum IntroProvider
    {
        Tmdb,
        Tvdb,
        Imdb,
    }

    public sealed class ProviderSelection
    {
        public IntroProvider Provider { get; set; }
        public string Id { get; set; }

        public string ParameterName
        {
            get
            {
                switch (Provider)
                {
                    case IntroProvider.Tmdb: return "tmdb_id";
                    case IntroProvider.Tvdb: return "tvdb_id";
                    default: return "imdb_id";
                }
            }
        }
    }

    public sealed class IntroSegment
    {
        public double? StartMilliseconds { get; set; }
        public double? EndMilliseconds { get; set; }
    }

    public sealed class IntroMarkerRange
    {
        public long StartTicks { get; set; }
        public long EndTicks { get; set; }

        public double StartSeconds => StartTicks / 10000000.0;
        public double EndSeconds => EndTicks / 10000000.0;
    }

    public static class IntroImportHelpers
    {
        private const long TicksPerSecond = 10000000;
        private const long TicksPerMillisecond = 10000;

        public static ProviderSelection SelectProvider(IDictionary<string, string> providerIds)
        {
            if (providerIds == null)
            {
                return null;
            }

            return SelectProvider(providerIds, IntroProvider.Tmdb, "Tmdb", "TMDb", "TheMovieDb")
                ?? SelectProvider(providerIds, IntroProvider.Tvdb, "Tvdb", "TVDb", "TheTVDB")
                ?? SelectProvider(providerIds, IntroProvider.Imdb, "Imdb", "IMDB");
        }

        public static List<ProviderSelection> SelectProviders(IDictionary<string, string> providerIds)
        {
            var result = new List<ProviderSelection>();
            if (providerIds == null)
            {
                return result;
            }

            var tmdb = SelectProvider(providerIds, IntroProvider.Tmdb, "Tmdb", "TMDb", "TheMovieDb");
            var tvdb = SelectProvider(providerIds, IntroProvider.Tvdb, "Tvdb", "TVDb", "TheTVDB");
            var imdb = SelectProvider(providerIds, IntroProvider.Imdb, "Imdb", "IMDB");
            if (tmdb != null) result.Add(tmdb);
            if (tvdb != null) result.Add(tvdb);
            if (imdb != null) result.Add(imdb);
            return result;
        }

        public static bool TryParseResponse(string json, out IntroSegment segment, out bool hasIntro)
        {
            segment = null;
            hasIntro = false;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(IntroResponse));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var response = serializer.ReadObject(stream) as IntroResponse;
                    if (response == null)
                    {
                        return false;
                    }

                    hasIntro = response.Intro != null && response.Intro.Count > 0;
                    if (hasIntro)
                    {
                        segment = response.Intro[0];
                        // TheIntroDB represents an intro that starts at the beginning with null start_ms.
                        if (!segment.StartMilliseconds.HasValue)
                        {
                            segment.StartMilliseconds = 0;
                        }
                    }

                    return true;
                }
            }
            catch (SerializationException)
            {
                return false;
            }
            catch (InvalidDataContractException)
            {
                return false;
            }
        }

        public static bool TryValidate(IntroSegment segment, double minSeconds, double maxSeconds,
                                       long? runtimeTicks, out IntroMarkerRange range)
        {
            range = null;
            if (segment == null || !segment.StartMilliseconds.HasValue || !segment.EndMilliseconds.HasValue
                || !IsFinite(segment.StartMilliseconds.Value) || !IsFinite(segment.EndMilliseconds.Value)
                || !IsFinite(minSeconds) || !IsFinite(maxSeconds)
                || minSeconds < 0 || maxSeconds < minSeconds)
            {
                return false;
            }

            var startMs = segment.StartMilliseconds.Value;
            var endMs = segment.EndMilliseconds.Value;
            if (startMs < 0 || endMs <= startMs)
            {
                return false;
            }

            var durationSeconds = (endMs - startMs) / 1000.0;
            if (durationSeconds < minSeconds || durationSeconds > maxSeconds)
            {
                return false;
            }

            if (runtimeTicks.HasValue && runtimeTicks.Value > 0
                && endMs > runtimeTicks.Value / (double)TicksPerMillisecond)
            {
                return false;
            }

            if (startMs > long.MaxValue / (double)TicksPerMillisecond
                || endMs > long.MaxValue / (double)TicksPerMillisecond)
            {
                return false;
            }

            range = new IntroMarkerRange
            {
                StartTicks = checked((long)Math.Round(startMs * TicksPerMillisecond, MidpointRounding.AwayFromZero)),
                EndTicks = checked((long)Math.Round(endMs * TicksPerMillisecond, MidpointRounding.AwayFromZero)),
            };
            return range.EndTicks > range.StartTicks;
        }

        public static bool HasValidIntroPair(IEnumerable<ChapterInfo> chapters)
        {
            if (chapters == null)
            {
                return false;
            }

            long? earliestStart = null;
            var ends = new List<long>();
            foreach (var chapter in chapters)
            {
                if (chapter == null)
                {
                    continue;
                }

                if (chapter.MarkerType == MarkerType.IntroStart)
                {
                    if (!earliestStart.HasValue || chapter.StartPositionTicks < earliestStart.Value)
                    {
                        earliestStart = chapter.StartPositionTicks;
                    }
                }
                else if (chapter.MarkerType == MarkerType.IntroEnd)
                {
                    ends.Add(chapter.StartPositionTicks);
                }
            }

            if (!earliestStart.HasValue)
            {
                return false;
            }

            foreach (var end in ends)
            {
                if (end > earliestStart.Value)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasAnyIntroMarker(IEnumerable<ChapterInfo> chapters)
        {
            return chapters != null && chapters.Any(chapter => chapter != null
                && (chapter.MarkerType == MarkerType.IntroStart || chapter.MarkerType == MarkerType.IntroEnd));
        }

        public static string BuildEpisodeKey(ProviderSelection provider, int season, int episode)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.Id) || season < 0 || episode < 0)
            {
                return null;
            }

            return ProviderKey(provider.Provider) + ":" + provider.Id.Trim().ToUpperInvariant() + ":"
                + season.ToString(CultureInfo.InvariantCulture) + ":"
                + episode.ToString(CultureInfo.InvariantCulture);
        }

        public static bool AreRuntimesCompatible(long? targetRuntimeTicks, long? sourceRuntimeTicks)
        {
            if (!sourceRuntimeTicks.HasValue || sourceRuntimeTicks.Value <= 0)
            {
                return false;
            }

            if (!targetRuntimeTicks.HasValue || targetRuntimeTicks.Value <= 0)
            {
                return true;
            }

            var largerRuntime = Math.Max(targetRuntimeTicks.Value, sourceRuntimeTicks.Value);
            var tolerance = Math.Max(5.0 * TicksPerSecond, largerRuntime * 0.01);
            return Math.Abs((double)targetRuntimeTicks.Value - sourceRuntimeTicks.Value) <= tolerance;
        }

        public static bool TryExtractMarkerRange(IEnumerable<ChapterInfo> chapters, double minSeconds,
                                                 double maxSeconds, long? runtimeTicks,
                                                 out IntroMarkerRange range)
        {
            range = null;
            if (chapters == null)
            {
                return false;
            }

            long? earliestStart = null;
            var ends = new List<long>();
            foreach (var chapter in chapters)
            {
                if (chapter == null)
                {
                    continue;
                }

                if (chapter.MarkerType == MarkerType.IntroStart)
                {
                    if (!earliestStart.HasValue || chapter.StartPositionTicks < earliestStart.Value)
                    {
                        earliestStart = chapter.StartPositionTicks;
                    }
                }
                else if (chapter.MarkerType == MarkerType.IntroEnd)
                {
                    ends.Add(chapter.StartPositionTicks);
                }
            }

            if (!earliestStart.HasValue)
            {
                return false;
            }

            long? earliestEnd = null;
            foreach (var end in ends)
            {
                if (end > earliestStart.Value && (!earliestEnd.HasValue || end < earliestEnd.Value))
                {
                    earliestEnd = end;
                }
            }

            if (!earliestEnd.HasValue)
            {
                return false;
            }

            return TryValidate(new IntroSegment
            {
                StartMilliseconds = earliestStart.Value / (double)TicksPerMillisecond,
                EndMilliseconds = earliestEnd.Value / (double)TicksPerMillisecond,
            }, minSeconds, maxSeconds, runtimeTicks, out range);
        }

        public static bool HasMateriallyConflictingRanges(IEnumerable<IntroMarkerRange> ranges)
        {
            if (ranges == null)
            {
                return false;
            }

            var observed = new List<IntroMarkerRange>();
            foreach (var range in ranges)
            {
                if (range == null)
                {
                    continue;
                }

                foreach (var previous in observed)
                {
                    if (Math.Abs((double)range.StartTicks - previous.StartTicks) > 5.0 * TicksPerSecond
                        || Math.Abs((double)range.EndTicks - previous.EndTicks) > 5.0 * TicksPerSecond)
                    {
                        return true;
                    }
                }

                observed.Add(range);
            }

            return false;
        }

        public static List<ChapterInfo> MergeChapters(IEnumerable<ChapterInfo> chapters, IntroMarkerRange range)
        {
            if (range == null || range.StartTicks < 0 || range.EndTicks <= range.StartTicks)
            {
                throw new ArgumentException("A valid intro marker range is required.", nameof(range));
            }

            var merged = chapters == null ? new List<ChapterInfo>() : new List<ChapterInfo>(chapters);
            merged.Add(new ChapterInfo
            {
                StartPositionTicks = range.StartTicks,
                MarkerType = MarkerType.IntroStart,
            });
            merged.Add(new ChapterInfo
            {
                StartPositionTicks = range.EndTicks,
                MarkerType = MarkerType.IntroEnd,
            });
            return merged.OrderBy(chapter => chapter == null ? long.MaxValue : chapter.StartPositionTicks).ToList();
        }

        public static string BuildRequestUrl(ProviderSelection provider, int season, int episode, long? runtimeTicks)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
            {
                throw new ArgumentException("A provider ID is required.", nameof(provider));
            }

            var url = "https://api.theintrodb.org/v3/media?" + provider.ParameterName + "="
                + Uri.EscapeDataString(provider.Id) + "&season=" + season.ToString(CultureInfo.InvariantCulture)
                + "&episode=" + episode.ToString(CultureInfo.InvariantCulture);
            if (runtimeTicks.HasValue && runtimeTicks.Value > 0)
            {
                url += "&duration_ms=" + (runtimeTicks.Value / TicksPerMillisecond).ToString(CultureInfo.InvariantCulture);
            }

            return url;
        }

        private static string ProviderKey(IntroProvider provider)
        {
            switch (provider)
            {
                case IntroProvider.Tmdb: return "tmdb";
                case IntroProvider.Tvdb: return "tvdb";
                default: return "imdb";
            }
        }

        private static ProviderSelection SelectProvider(IDictionary<string, string> providerIds, IntroProvider provider,
                                                        params string[] names)
        {
            foreach (var pair in providerIds)
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                foreach (var name in names)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ProviderSelection { Provider = provider, Id = pair.Value.Trim() };
                    }
                }
            }

            return null;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        [DataContract]
        private sealed class IntroResponse
        {
            [DataMember(Name = "intro")]
            public List<IntroSegmentContract> Intro { get; set; }
        }

        [DataContract]
        private sealed class IntroSegmentContract
        {
            [DataMember(Name = "start_ms")]
            public double? StartMilliseconds { get; set; }

            [DataMember(Name = "end_ms")]
            public double? EndMilliseconds { get; set; }

            public static implicit operator IntroSegment(IntroSegmentContract contract)
            {
                return contract == null
                    ? null
                    : new IntroSegment
                    {
                        StartMilliseconds = contract.StartMilliseconds,
                        EndMilliseconds = contract.EndMilliseconds,
                    };
            }
        }
    }
}
