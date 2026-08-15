using System;
using System.Collections.Generic;
using EmbyThemeMaker.Theme;
using Xunit;

namespace EmbyThemeMaker.Tests
{
    public class FfmpegEnvironmentTests
    {
        [Fact]
        public void BundledLibraryPathAddsExistingInstallationDirectoriesAndPreservesExistingValue()
        {
            var existingDirectories = new HashSet<string>(StringComparer.Ordinal)
            {
                "/app/emby/lib",
                "/app/emby/extra/lib",
            };

            var value = FfmpegRunner.BuildBundledLibraryPath(
                "/app/emby/bin/ffmpeg",
                "/custom/lib",
                existingDirectories.Contains,
                true);

            Assert.Equal("/app/emby/lib:/app/emby/extra/lib:/custom/lib", value);
        }

        [Fact]
        public void BundledLibraryPathIgnoresMissingDirectories()
        {
            var value = FfmpegRunner.BuildBundledLibraryPath(
                "/opt/emby/bin/ffprobe",
                null,
                path => path == "/opt/emby/lib",
                true);

            Assert.Equal("/opt/emby/lib", value);
        }

        [Fact]
        public void BundledLibraryPathPreservesWhitespaceOnlyExistingValue()
        {
            var value = FfmpegRunner.BuildBundledLibraryPath(
                "/app/emby/bin/ffmpeg",
                " ",
                path => path == "/app/emby/lib",
                true);

            Assert.Equal("/app/emby/lib: ", value);
        }

        [Fact]
        public void BundledLibraryPathPreservesEmptyExistingValue()
        {
            var value = FfmpegRunner.BuildBundledLibraryPath(
                "/app/emby/bin/ffmpeg",
                string.Empty,
                path => path == "/app/emby/lib",
                true);

            Assert.Equal("/app/emby/lib:", value);
        }

        [Fact]
        public void BundledLibraryPathLeavesNonUnixEnvironmentUnchanged()
        {
            var value = FfmpegRunner.BuildBundledLibraryPath(
                "C:\\Emby\\ffmpeg.exe",
                "C:\\existing",
                path => true,
                false);

            Assert.Equal("C:\\existing", value);
        }
    }
}
