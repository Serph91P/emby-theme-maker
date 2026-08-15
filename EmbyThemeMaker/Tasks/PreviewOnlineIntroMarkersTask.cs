using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;

namespace EmbyThemeMaker.Tasks
{
    /// <summary>Reports valid local or online intro candidates without changing episode chapters.</summary>
    public class PreviewOnlineIntroMarkersTask : OnlineIntroTaskBase
    {
        public PreviewOnlineIntroMarkersTask(ILibraryManager libraryManager, IItemRepository itemRepository,
                                             ILogManager logManager)
            : base(libraryManager, itemRepository, logManager)
        {
        }

        public override string Name => "Theme Maker: Preview Local and Online Intro Markers (read-only)";
        public override string Key => "ThemeMakerPreviewOnlineIntroMarkers";
        public override string Description =>
            "Find validated local or TheIntroDB markers for eligible .strm episodes without writing chapters.";
        protected override bool Preview => true;
    }
}
