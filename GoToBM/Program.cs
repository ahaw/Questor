/* Written by Noob536 */

namespace GoToBM
{
    using DirectEve;
    using System;
    using Questor.Modules.Activities;
    using Questor.Modules.Caching;
    using Questor.Modules.Logging;
    using Questor.Modules.Actions;
    using Questor.Modules.States;
    using Questor.Modules.BackgroundTasks;
    
    static class Program
    {
        private static DirectEve _directEve;
        private static Traveler _traveler;
        private static Cleanup _cleanup;
        private static Defense _defense;
        private static DirectBookmark _bookmark;
        private static DateTime _lastPulse;
        private static bool _done = false;
        private static string _BM;
        private static bool _started = false;

        [STAThread]
        static void Main(string[] args)
        {
            Logging.Log("GoToBM","Started",Logging.white);
            if (args.Length == 0 || args[0].Length < 1)
            {
                Logging.Log("GoToBM"," You need to supply a bookmark name",Logging.white);
                Logging.Log("GoToBM"," Ended",Logging.white);
                return;
            }
            _BM = args[0];
            _BM = _BM.ToLower();

            _directEve = new DirectEve();
            Cache.Instance.DirectEve = _directEve;
            _directEve.OnFrame += OnFrame;
            _traveler = new Traveler();
            _cleanup = new Cleanup();
            _defense = new Defense();

            while (!_done)
            {
                System.Threading.Thread.Sleep(50);
            }

            _directEve.Dispose();
            Logging.Log("GoToBM"," Exiting",Logging.white);
            return;
        }

        static void OnFrame(object sender, EventArgs e)
        {
            if (DateTime.Now.Subtract(_lastPulse).TotalMilliseconds < 1500)
                return;
            _lastPulse = DateTime.Now;

            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            Cache.Instance.LastFrame = DateTime.Now;

            // Only pulse state changes every 1.5s
            if (DateTime.Now.Subtract(_lastPulse).TotalMilliseconds < 300)
                return;
            _lastPulse = DateTime.Now;

            // Session is not ready yet, do not continue
            if (!Cache.Instance.DirectEve.Session.IsReady)
                return;

            if (Cache.Instance.DirectEve.Session.IsReady)
                Cache.Instance.LastSessionIsReady = DateTime.Now;

            // We are not in space or station, don't do shit yet!
            if (!Cache.Instance.InSpace && !Cache.Instance.InStation)
            {
                Cache.Instance.NextInSpaceorInStation = DateTime.Now.AddSeconds(12);
                Cache.Instance.LastSessionChange = DateTime.Now;
                return;
            }

            if (DateTime.Now < Cache.Instance.NextInSpaceorInStation)
                return;

            // We always check our defense state if we're in space, regardless of questor state
            // We also always check panic
            if (Cache.Instance.InSpace)
            {
                if (!Cache.Instance.DoNotBreakInvul)
                {
                    _defense.ProcessState();
                }
            }

            // Start _cleanup.ProcessState
            // Description: Closes Windows, and eventually other things considered 'cleanup' useful to more than just Questor(Missions) but also Anomalies, Mining, etc
            //
            _cleanup.ProcessState();

            // Done
            // Cleanup State: ProcessState

            if (Cache.Instance.InWarp)
                return;

            if (!_started)
            {
                _started = true;
                if (!Cache.Instance.DirectEve.Session.IsReady)
                {

                    Logging.Log("GoToBM"," Not in game, exiting",Logging.white);
                    return;
                }
                Logging.Log("GoToBM",": Attempting to find bookmark [" + _BM + "]",Logging.white);
                foreach (var bookmark in Cache.Instance.DirectEve.Bookmarks)
                {
                    if (bookmark.Title.ToLower().Equals(_BM))
                    {
                        _bookmark = bookmark;
                        break;
                    }
                    if (_bookmark == null && bookmark.Title.ToLower().Contains(_BM))
                    {
                        _bookmark = bookmark;
                    }
                }
                if (_bookmark == null)
                {
                    Logging.Log("GoToBM",": Bookmark not found",Logging.white);
                    _done = true;
                    return;
                }
                _traveler.Destination = new BookmarkDestination(_bookmark);
            }
            _traveler.ProcessState();
            if (_States.CurrentTravelerState == TravelerState.AtDestination)
            {
                _done = true;
                Logging.Log("GoToBM"," At destination",Logging.white);
            }
            else if (_States.CurrentTravelerState == TravelerState.Error)
            {
                Logging.Log("GoToBM"," Traveler error",Logging.white);
                _done = true;
            }
        }
    }
}