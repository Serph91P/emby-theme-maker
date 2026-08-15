using System.Collections.Generic;
using EmbyThemeMaker.OnlineIntro;
using MediaBrowser.Model.Entities;
using Xunit;

namespace EmbyThemeMaker.Tests
{
    public class IntroImportHelpersTests
    {
        [Fact]
        public void SelectProviderPrefersTmdbThenTvdbThenImdb()
        {
            var all = new Dictionary<string, string>
            {
                ["Tmdb"] = "100",
                ["Tvdb"] = "200",
                ["Imdb"] = "tt300",
            };

            var selection = IntroImportHelpers.SelectProvider(all);

            Assert.Equal(IntroProvider.Tmdb, selection.Provider);
            Assert.Equal("100", selection.Id);
            Assert.Equal("tvdb_id", IntroImportHelpers.SelectProvider(new Dictionary<string, string>
            {
                ["Tvdb"] = "200",
                ["Imdb"] = "tt300",
            }).ParameterName);
            Assert.Equal("tmdb_id", IntroImportHelpers.SelectProvider(new Dictionary<string, string>
            {
                ["tmdb"] = "99",
            }).ParameterName);
            Assert.Equal("imdb_id", IntroImportHelpers.SelectProvider(new Dictionary<string, string>
            {
                ["Imdb"] = "tt300",
            }).ParameterName);
        }

        [Fact]
        public void BuildEpisodeKeyUsesTheSelectedProviderAndNormalizesItsId()
        {
            var key = IntroImportHelpers.BuildEpisodeKey(new ProviderSelection
            {
                Provider = IntroProvider.Imdb,
                Id = " tt12345 ",
            }, 0, 7);

            Assert.Equal("imdb:TT12345:0:7", key);
            Assert.Null(IntroImportHelpers.BuildEpisodeKey(null, 1, 1));
            Assert.Null(IntroImportHelpers.BuildEpisodeKey(new ProviderSelection
            {
                Provider = IntroProvider.Tmdb,
                Id = " ",
            }, 1, 1));
        }

        [Fact]
        public void RuntimeCompatibilityAllowsUnknownTargetButRequiresKnownSource()
        {
            const long ticksPerSecond = 10000000;

            Assert.True(IntroImportHelpers.AreRuntimesCompatible(null, 60 * ticksPerSecond));
            Assert.True(IntroImportHelpers.AreRuntimesCompatible(0, 60 * ticksPerSecond));
            Assert.False(IntroImportHelpers.AreRuntimesCompatible(60 * ticksPerSecond, null));
            Assert.True(IntroImportHelpers.AreRuntimesCompatible(60 * ticksPerSecond, 64 * ticksPerSecond));
            Assert.False(IntroImportHelpers.AreRuntimesCompatible(60 * ticksPerSecond, 66 * ticksPerSecond));
            Assert.True(IntroImportHelpers.AreRuntimesCompatible(1000 * ticksPerSecond, 1009 * ticksPerSecond));
            Assert.False(IntroImportHelpers.AreRuntimesCompatible(1000 * ticksPerSecond, 1011 * ticksPerSecond));
        }

        [Fact]
        public void ParseResponseReadsFirstIntroSegmentAndDistinguishesMisses()
        {
            IntroSegment segment;
            bool hasIntro;

            Assert.True(IntroImportHelpers.TryParseResponse(
                "{\"intro\":[{\"start_ms\":12000,\"end_ms\":42000}]}", out segment, out hasIntro));
            Assert.True(hasIntro);
            Assert.Equal(12000, segment.StartMilliseconds);
            Assert.Equal(42000, segment.EndMilliseconds);

            Assert.True(IntroImportHelpers.TryParseResponse("{\"intro\":[]}", out segment, out hasIntro));
            Assert.False(hasIntro);
            Assert.Null(segment);
            Assert.False(IntroImportHelpers.TryParseResponse("not json", out segment, out hasIntro));
        }

        [Fact]
        public void ParseResponseTreatsNullIntroStartAsZero()
        {
            IntroSegment segment;
            bool hasIntro;

            Assert.True(IntroImportHelpers.TryParseResponse(
                "{\"intro\":[{\"start_ms\":null,\"end_ms\":23000}]}", out segment, out hasIntro));
            Assert.True(hasIntro);
            Assert.Equal(0, segment.StartMilliseconds);
            Assert.Equal(23000, segment.EndMilliseconds);
        }

        [Theory]
        [InlineData(12000, 42000, true)]
        [InlineData(-1, 42000, false)]
        [InlineData(12000, 12000, false)]
        [InlineData(12000, 170000, false)]
        public void ValidateRejectsInvalidAndImplausibleRanges(double startMs, double endMs, bool expected)
        {
            IntroMarkerRange range;
            var valid = IntroImportHelpers.TryValidate(new IntroSegment
            {
                StartMilliseconds = startMs,
                EndMilliseconds = endMs,
            }, 8, 150, null, out range);

            Assert.Equal(expected, valid);
            if (expected)
            {
                Assert.Equal(120000000, range.StartTicks);
                Assert.Equal(420000000, range.EndTicks);
            }
        }

        [Fact]
        public void ValidateRejectsEndBeyondKnownRuntime()
        {
            IntroMarkerRange range;
            Assert.False(IntroImportHelpers.TryValidate(new IntroSegment
            {
                StartMilliseconds = 12000,
                EndMilliseconds = 42000,
            }, 8, 150, 400000000, out range));
        }

        [Fact]
        public void ExtractRangeFindsTheEarliestValidMarkerPairAndValidatesDuration()
        {
            IntroMarkerRange range;
            Assert.True(IntroImportHelpers.TryExtractMarkerRange(new[]
            {
                new ChapterInfo { MarkerType = MarkerType.IntroEnd, StartPositionTicks = 100000000 },
                new ChapterInfo { MarkerType = MarkerType.IntroEnd, StartPositionTicks = 420000000 },
                new ChapterInfo { MarkerType = MarkerType.IntroStart, StartPositionTicks = 120000000 },
            }, 8, 150, 500000000, out range));

            Assert.Equal(120000000, range.StartTicks);
            Assert.Equal(420000000, range.EndTicks);
            Assert.False(IntroImportHelpers.TryExtractMarkerRange(new[]
            {
                new ChapterInfo { MarkerType = MarkerType.IntroStart, StartPositionTicks = 120000000 },
                new ChapterInfo { MarkerType = MarkerType.IntroEnd, StartPositionTicks = 121000000 },
            }, 8, 150, null, out range));
        }

        [Fact]
        public void MaterialConflictAllowsSmallDifferencesButRejectsDivergentRanges()
        {
            var first = new IntroMarkerRange { StartTicks = 120000000, EndTicks = 420000000 };
            var close = new IntroMarkerRange { StartTicks = 160000000, EndTicks = 460000000 };
            var conflicting = new IntroMarkerRange { StartTicks = 180000000, EndTicks = 480000000 };

            Assert.False(IntroImportHelpers.HasMateriallyConflictingRanges(new[] { first, close }));
            Assert.True(IntroImportHelpers.HasMateriallyConflictingRanges(new[] { first, conflicting }));
        }

        [Fact]
        public void MergeChaptersPreservesExistingChaptersAndAddsIntroMarkers()
        {
            var chapters = new List<ChapterInfo>
            {
                new ChapterInfo { Name = "Opening", StartPositionTicks = 50000000 },
            };
            var merged = IntroImportHelpers.MergeChapters(chapters, new IntroMarkerRange
            {
                StartTicks = 120000000,
                EndTicks = 420000000,
            });

            Assert.Equal(3, merged.Count);
            Assert.Same(chapters[0], merged[0]);
            Assert.Equal(MarkerType.IntroStart, merged[1].MarkerType);
            Assert.Equal(MarkerType.IntroEnd, merged[2].MarkerType);
            Assert.True(IntroImportHelpers.HasValidIntroPair(merged));
        }

        [Fact]
        public void AnyDanglingIntroMarkerBlocksImport()
        {
            Assert.True(IntroImportHelpers.HasAnyIntroMarker(new[]
            {
                new ChapterInfo { MarkerType = MarkerType.IntroStart, StartPositionTicks = 1 },
            }));
            Assert.False(IntroImportHelpers.HasAnyIntroMarker(new[]
            {
                new ChapterInfo { StartPositionTicks = 1 },
            }));
        }

        [Fact]
        public void MergeChaptersSortsWithoutDroppingExistingObjects()
        {
            var later = new ChapterInfo { Name = "Later", StartPositionTicks = 600000000 };
            var merged = IntroImportHelpers.MergeChapters(new[] { later }, new IntroMarkerRange
            {
                StartTicks = 120000000,
                EndTicks = 420000000,
            });

            Assert.Equal(MarkerType.IntroStart, merged[0].MarkerType);
            Assert.Equal(MarkerType.IntroEnd, merged[1].MarkerType);
            Assert.Same(later, merged[2]);
        }

        [Fact]
        public void SelectProvidersReturnsEverySupportedIdentityInPriorityOrder()
        {
            var providers = IntroImportHelpers.SelectProviders(new Dictionary<string, string>
            {
                ["Imdb"] = "tt300",
                ["Tvdb"] = "200",
                ["Tmdb"] = "100",
            });

            Assert.Equal(3, providers.Count);
            Assert.Equal(IntroProvider.Tmdb, providers[0].Provider);
            Assert.Equal(IntroProvider.Tvdb, providers[1].Provider);
            Assert.Equal(IntroProvider.Imdb, providers[2].Provider);
        }

        [Fact]
        public void ValidateRejectsNonFiniteOrMissingTimestamps()
        {
            IntroMarkerRange range;

            Assert.False(IntroImportHelpers.TryValidate(new IntroSegment { StartMilliseconds = double.NaN, EndMilliseconds = 10000 },
                5, 90, null, out range));
            Assert.False(IntroImportHelpers.TryValidate(new IntroSegment { StartMilliseconds = null, EndMilliseconds = 10000 },
                5, 90, null, out range));
        }

        [Fact]
        public void HasValidIntroPairRequiresEndAfterStart()
        {
            Assert.False(IntroImportHelpers.HasValidIntroPair(new[]
            {
                new ChapterInfo { MarkerType = MarkerType.IntroEnd, StartPositionTicks = 100 },
                new ChapterInfo { MarkerType = MarkerType.IntroStart, StartPositionTicks = 200 },
            }));
        }
    }
}
