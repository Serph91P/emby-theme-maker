using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;

namespace EmbyThemeMaker.Tasks
{
    /// <summary>Adds validated local or online intro markers while preserving existing chapters.</summary>
    public class ApplyOnlineIntroMarkersTask : OnlineIntroTaskBase
    {
        public ApplyOnlineIntroMarkersTask(ILibraryManager libraryManager, IItemRepository itemRepository,
                                           ILogManager logManager)
            : base(libraryManager, itemRepository, logManager)
        {
        }

        public override string Name => "Theme Maker: Apply Local and Online Intro Markers";
        public override string Key => "ThemeMakerApplyOnlineIntroMarkers";
        public override string Description =>
            "Copy validated local markers or import TheIntroDB markers for eligible .strm episodes without a valid existing pair.";
        protected override bool Preview => false;
    }
}
