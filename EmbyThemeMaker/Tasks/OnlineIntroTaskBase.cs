using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmbyThemeMaker.OnlineIntro;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace EmbyThemeMaker.Tasks
{
    /// <summary>Shared scheduled-task plumbing for read-only and applying local and online intro imports.</summary>
    public abstract class OnlineIntroTaskBase : IScheduledTask, IConfigurableScheduledTask
    {
        private static readonly SemaphoreSlim ImportGate = new SemaphoreSlim(1, 1);
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;

        protected OnlineIntroTaskBase(ILibraryManager libraryManager, IItemRepository itemRepository,
                                      ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = logManager.GetLogger("ThemeMaker");
        }

        public abstract string Name { get; }
        public abstract string Key { get; }
        public abstract string Description { get; }
        protected abstract bool Preview { get; }

        public string Category => "Theme Maker";
        public bool IsEnabled => true;
        public bool IsHidden => false;
        public bool IsLogged => true;

        // No automatic trigger. Users may run these manually or add their own schedule.
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            return Task.Run(async () =>
            {
                await ImportGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var options = Plugin.Instance?.Options;
                    if (options == null)
                    {
                        _logger.Error("[ThemeMaker] intro import options unavailable; aborting task");
                        return;
                    }

                    var engine = new OnlineIntroImportEngine(_libraryManager, _itemRepository, _logger);
                    engine.Run(options, Preview, progress, cancellationToken);
                }
                finally
                {
                    ImportGate.Release();
                }
            }, cancellationToken);
        }
    }
}
