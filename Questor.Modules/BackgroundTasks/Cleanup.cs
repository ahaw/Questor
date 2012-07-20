
namespace Questor.Modules.BackgroundTasks
{
    using System;
    using System.Globalization;
    using DirectEve;
    using System.Diagnostics;
    using LavishScriptAPI;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;

    public class Cleanup
    {
        private static DateTime _lastCleanupAction;
        private DateTime _lastCleanupProcessState;
        private int _dronebayclosingattempts = 0;

        //private DateTime _lastChatWindowAction;
        //private bool _newprivateconvowindowhandled;
        private static DateTime CloseQuestorDelay { get; set; }

        private static bool _closeQuestor10SecWarningDone = false;
        private static bool _closeQuestorCMDUplink = true;
        public static bool CloseQuestorflag = true;

        private void BeginClosingQuestor()
        {
            Cache.Instance.EnteredCloseQuestor_DateTime = DateTime.Now;
            Cache.Instance.SessionState = "Quitting";
        }

        public static bool CloseQuestor()
        {
            // 30 seconds + 10 to 90 seconds + 1 to 9 seconds before restarting
            int secRestart = (300 * 1) + Cache.Instance.RandomNumber(1, 9) * 100 + Cache.Instance.RandomNumber(1, 9) * 10;
            Cache.Instance.SessionState = "Quitting!!"; //so that IF we changed the state we would not be caught in a loop of re-entering closequestor
            if (!Cache.Instance.CloseQuestorCMDLogoff && !Cache.Instance.CloseQuestorCMDExitGame)
            {
                Cache.Instance.CloseQuestorCMDExitGame = true;
            }
            //if (_traveler.State == TravelerState.Idle)
            //{
            //    Logging.Log(
            //        "QuestorState.CloseQuestor: Entered Traveler - making sure we will be docked at Home Station");
            //}
            //AvoidBumpingThings();
            //TravelToAgentsStation();

            //if (_traveler.State == TravelerState.AtDestination ||
            //    DateTime.Now.Subtract(Cache.Instance.EnteredCloseQuestor_DateTime).TotalSeconds >
            //   Settings.Instance.SecondstoWaitAfterExteringCloseQuestorBeforeExitingEVE)
            //{
            //Logging.Log("QuestorState.CloseQuestor: At Station: Docked");
            // Write to Session log
            if (!Statistics.WriteSessionLogClosing()) return false;

            if (Settings.Instance.AutoStart && Settings.Instance.CloseQuestorAllowRestart)
            //if autostart is disabled do not schedule a restart of questor - let it stop gracefully.
            {
                if (Cache.Instance.CloseQuestorCMDLogoff)
                {
                    if (CloseQuestorflag)
                    {
                        Logging.Log("Questor", "Logging off EVE: In theory eve and questor will restart on their own when the client comes back up", Logging.white);
                        if (Settings.Instance.UseInnerspace)
                            LavishScript.ExecuteCommand("uplink echo Logging off EVE:  \\\"${Game}\\\" \\\"${Profile}\\\"");
                        Logging.Log("Questor", "you can change this option by setting the wallet and eveprocessmemoryceiling options to use exit instead of logoff: see the settings.xml file", Logging.white);
                        Logging.Log("Questor", "Logging Off eve in 15 seconds.", Logging.white);
                        CloseQuestorflag = false;
                        CloseQuestorDelay =
                            DateTime.Now.AddSeconds(Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                    }
                    if (CloseQuestorDelay.AddSeconds(-10) < DateTime.Now)
                    {
                        Logging.Log("Questor", "Exiting eve in 10 seconds", Logging.white);
                    }
                    if (CloseQuestorDelay < DateTime.Now)
                    {
                        Logging.Log("Questor", "Exiting eve now.", Logging.white);
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdLogOff);
                    }
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdLogOff);
                    return true;
                }
                if (Cache.Instance.CloseQuestorCMDExitGame)
                {
                    if (Settings.Instance.UseInnerspace)
                    {
                        //Logging.Log("Questor: We are in station: Exit option has been configured.");
                        if (((Settings.Instance.CloseQuestorArbitraryOSCmd) &&
                             (Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet)) ||
                            (Settings.Instance.CloseQuestorArbitraryOSCmd) &&
                            (Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile))
                        {
                            Logging.Log(
                                    "Questor", "You can't combine CloseQuestorArbitraryOSCmd with either of the other two options, fix your settings", Logging.white);
                        }
                        else
                        {
                            if ((Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet) &&
                                (Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile))
                            {
                                Logging.Log(
                                        "Questor", "You cant use both the CloseQuestorCMDUplinkIsboxerProfile and the CloseQuestorCMDUplinkIsboxerProfile setting, choose one", Logging.white);
                            }
                            else
                            {
                                if (Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile)
                                //if configured as true we will use the innerspace profile to restart this session
                                {
                                    //Logging.Log("Questor: We are in station: CloseQuestorCMDUplinkInnerspaceProfile is ["+ CloseQuestorCMDUplinkInnerspaceProfile.tostring() +"]");
                                    if (_closeQuestorCMDUplink)
                                    {
                                        Logging.Log(
                                                "Questor", "Starting a timer in the innerspace uplink to restart this innerspace profile session", Logging.white);
                                        LavishScript.ExecuteCommand("uplink exec Echo [${Time}] " +
                                                                    Settings.Instance.CharacterName +
                                                                    "'s Questor is starting a timedcommand to restart itself in a moment");
                                        LavishScript.ExecuteCommand(
                                            "uplink exec Echo [${Time}] timedcommand " + secRestart + " open \\\"${Game}\\\" \\\"${Profile}\\\"");
                                        LavishScript.ExecuteCommand(
                                            "uplink exec timedcommand " + secRestart + " open \\\"${Game}\\\" \\\"${Profile}\\\"");
                                        Logging.Log(
                                            "Questor", "Done: quitting this session so the new innerspace session can take over", Logging.white);
                                        Logging.Log("Questor", "Exiting eve in 15 seconds.", Logging.white);
                                        _closeQuestorCMDUplink = false;
                                        CloseQuestorDelay =
                                        DateTime.Now.AddSeconds(
                                            Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                                    }
                                    if ((CloseQuestorDelay.AddSeconds(-10) == DateTime.Now) &&
                                        (!_closeQuestor10SecWarningDone))
                                    {
                                        _closeQuestor10SecWarningDone = true;
                                        Logging.Log("Questor", "Exiting eve in 10 seconds", Logging.white);
                                        Cache.Instance.DirecteveDispose();
                                        if (Cache.Instance.CloseQuestorEndProcess)
                                        {
                                            Process.GetCurrentProcess().Kill();
                                            return false;
                                        }
                                        else
                                        {
                                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                            return false;
                                        }
                                    }
                                    if (CloseQuestorDelay < DateTime.Now)
                                    {
                                        Logging.Log("Questor", "Exiting eve now.", Logging.white);
                                        Cache.Instance.DirecteveDispose();
                                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                    }
                                    return false;
                                }
                                else if (Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet)
                                //if configured as true we will use isboxer to restart this session
                                {
                                    //Logging.Log("Questor: We are in station: CloseQuestorCMDUplinkIsboxerProfile is ["+ CloseQuestorCMDUplinkIsboxerProfile.tostring() +"]");
                                    if (_closeQuestorCMDUplink)
                                    {
                                        Logging.Log(
                                                "Questor", "Starting a timer in the innerspace uplink to restart this isboxer character set", Logging.white);
                                        LavishScript.ExecuteCommand("uplink exec Echo [${Time}] " +
                                                                    Settings.Instance.CharacterName +
                                                                    "'s Questor is starting a timedcommand to restart itself in a moment");
                                        LavishScript.ExecuteCommand(
                                            "uplink exec Echo [${Time}] timedcommand " + secRestart + " runscript isboxer -launchslot \\\"${ISBoxerCharacterSet}\\\" \\\"${ISBoxerSlot}\\\"");
                                        LavishScript.ExecuteCommand(
                                            "uplink timedcommand " + secRestart + " runscript isboxer -launchslot \\\"${ISBoxerCharacterSet}\\\" \\\"${ISBoxerSlot}\\\"");
                                        Logging.Log(
                                            "Questor", "Done: quitting this session so the new isboxer session can take over", Logging.white);
                                        Logging.Log("Questor", "Exiting eve.", Logging.white);
                                        _closeQuestorCMDUplink = false;
                                        CloseQuestorDelay =
                                        DateTime.Now.AddSeconds(
                                        Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                                    }
                                    if ((CloseQuestorDelay.AddSeconds(-10) == DateTime.Now) &&
                                        (!_closeQuestor10SecWarningDone))
                                    {
                                        _closeQuestor10SecWarningDone = true;
                                        Logging.Log("Questor", "Exiting eve in 10 seconds", Logging.white);
                                        Cache.Instance.DirecteveDispose();
                                        if (Cache.Instance.CloseQuestorEndProcess)
                                        {
                                            Process.GetCurrentProcess().Kill();
                                            return false;
                                        }
                                        else
                                        {
                                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                            return false;
                                        }
                                    }
                                    if (CloseQuestorDelay < DateTime.Now)
                                    {
                                        Logging.Log("Questor", "Exiting eve now.", Logging.white);
                                        Cache.Instance.DirecteveDispose();
                                        if (Cache.Instance.CloseQuestorEndProcess)
                                        {
                                            Process.GetCurrentProcess().Kill();
                                            return false;
                                        }
                                        else
                                        {
                                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                            return false;
                                        }
                                    }
                                    return false;
                                }
                                else if (Settings.Instance.CloseQuestorArbitraryOSCmd)
                                // will execute an arbitrary OS command through the IS Uplink
                                {
                                    if (_closeQuestorCMDUplink)
                                    {
                                        Logging.Log(
                                                "Questor", "Starting a timer in the innerspace uplink to execute an arbitrary OS command", Logging.white);
                                        LavishScript.ExecuteCommand("uplink exec Echo [${Time}] " +
                                                                    Settings.Instance.CharacterName +
                                                                    "'s Questor is starting a timedcommand to restart itself in a moment");
                                        LavishScript.ExecuteCommand(
                                            "uplink exec Echo [${Time}] timedcommand " + secRestart + " OSExecute " +
                                            Settings.Instance.CloseQuestorOSCmdContents.ToString(CultureInfo.InvariantCulture));
                                        LavishScript.ExecuteCommand(
                                            "uplink exec timedcommand " + secRestart + " OSExecute " +
                                            Settings.Instance.CloseQuestorOSCmdContents.ToString(CultureInfo.InvariantCulture));
                                        Logging.Log("Questor", "Done: quitting this session", Logging.white);
                                        Logging.Log("Questor", "Exiting eve in 15 seconds.", Logging.white);
                                        _closeQuestorCMDUplink = false;
                                        CloseQuestorDelay =
                                            DateTime.Now.AddSeconds(
                                                Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                                    }
                                    if ((CloseQuestorDelay.AddSeconds(-10) == DateTime.Now) &&
                                        (!_closeQuestor10SecWarningDone))
                                    {
                                        _closeQuestor10SecWarningDone = true;
                                        Logging.Log("Questor", ": Exiting eve in 10 seconds", Logging.white);
                                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                    }
                                    if (CloseQuestorDelay < DateTime.Now)
                                    {
                                        Logging.Log("Questor", "Exiting eve now.", Logging.white);
                                        Cache.Instance.DirecteveDispose();
                                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                    }
                                    return false;
                                }
                                else if (!Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile &&
                                         !Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet &&
                                         !Settings.Instance.CloseQuestorArbitraryOSCmd)
                                {
                                    Logging.Log(
                                            "Questor", "CloseQuestorArbitraryOSCmd, CloseQuestorCMDUplinkInnerspaceProfile and CloseQuestorCMDUplinkIsboxerProfile all false", Logging.white);
                                    if (_closeQuestorCMDUplink)
                                    {
                                        _closeQuestorCMDUplink = false;
                                        CloseQuestorDelay =
                                            DateTime.Now.AddSeconds(
                                                Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                                    }
                                    if ((CloseQuestorDelay.AddSeconds(-10) == DateTime.Now) &&
                                        (!_closeQuestor10SecWarningDone))
                                    {
                                        _closeQuestor10SecWarningDone = true;
                                        Logging.Log("Questor", "Exiting eve in 10 seconds", Logging.white);
                                        Cache.Instance.DirecteveDispose();
                                        if (Cache.Instance.CloseQuestorEndProcess)
                                        {
                                            Process.GetCurrentProcess().Kill();
                                            return false;
                                        }
                                        else
                                        {
                                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                            return false;
                                        }
                                    }
                                    if (CloseQuestorDelay < DateTime.Now)
                                    {
                                        Logging.Log("Questor", "Exiting eve now.", Logging.white);
                                        Cache.Instance.DirecteveDispose();
                                        if (Cache.Instance.CloseQuestorEndProcess)
                                        {
                                            Process.GetCurrentProcess().Kill();
                                            return false;
                                        }
                                        else
                                        {
                                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                            return false;
                                        }
                                    }
                                    return false;
                                }
                            }
                        }
                    }
                    else
                    {
                        Logging.Log("Questor", "CloseQuestor: We are configured to NOT use innerspace. useInnerspace = false", Logging.white);
                        Logging.Log("Questor", "CloseQuestor: Currently the questor will exit (and not restart itself) in this configuration, this likely needs additional work to make questor reentrant so we can use a scheduled task?!", Logging.white);
                        if ((CloseQuestorDelay.AddSeconds(-10) == DateTime.Now) &&
                                        (!_closeQuestor10SecWarningDone))
                        {
                            _closeQuestor10SecWarningDone = true;
                            Logging.Log("Questor", "Exiting eve in 10 seconds", Logging.white);
                            Cache.Instance.DirecteveDispose();
                            if (Cache.Instance.CloseQuestorEndProcess)
                            {
                                Process.GetCurrentProcess().Kill();
                                return false;
                            }
                            else
                            {
                                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                return false;
                            }
                        }
                        if (CloseQuestorDelay < DateTime.Now)
                        {
                            Logging.Log("Questor", "Exiting eve now.", Logging.white);
                            Cache.Instance.DirecteveDispose();
                            if (Cache.Instance.CloseQuestorEndProcess)
                            {
                                Process.GetCurrentProcess().Kill();
                                return false;
                            }
                            else
                            {
                                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                                return false;
                            }
                        }
                    }
                }
            }
            Logging.Log("Questor", "Autostart is false: Stopping EVE with quit command (if EVE is going to restart it will do so externally)", Logging.white);
            if (Cache.Instance.CloseQuestorEndProcess)
            {
                Logging.Log("Questor", "Closing with: Process.GetCurrentProcess().Kill()", Logging.white);
                Process.GetCurrentProcess().Kill();
                return false;
            }
            else
            {
                Logging.Log("Questor", "Closing with: DirectCmd.CmdQuitGame", Logging.white);
                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                return false;
            }
        }

        public static bool CloseInventoryWindows()
        {
            if (DateTime.Now < _lastCleanupAction.AddMilliseconds(500))
                return false;

            _lastCleanupAction = DateTime.Now;
            //
            // go through *every* window
            //
            foreach (DirectWindow window in Cache.Instance.Windows)
            {
                if (window.Name.Contains("_ShipDroneBay_") && window.Caption.Contains("Drone Bay") && window.Type.Contains("Inventory"))
                {
                    Logging.Log("Cleanup","CloseInventoryWindows: Closing Drone Bay Window",Logging.white);
                    window.Close();
                    _lastCleanupAction = DateTime.Now;
                    return false;
                }
                if (window.Name.Contains("_ShipCargo_") && window.Caption.Contains("active ship") && window.Type.Contains("Inventory"))                 
                {
                    Logging.Log("Cleanup", "CloseInventoryWindows: Closing Cargo Bay Window", Logging.white);
                    window.Close();
                    _lastCleanupAction = DateTime.Now;
                    return false;
                }
                if (window.Name.Contains("_StationItems_") && window.Caption.Contains("Item hangar") && window.Type.Contains("Inventory"))
                {
                    Logging.Log("Cleanup", "CloseInventoryWindows: Closing Item Hangar Window", Logging.white);
                    window.Close();
                    _lastCleanupAction = DateTime.Now;
                    return false;
                }
                if (window.Name.Contains("_StationShips_") && window.Caption.Contains("Ship hangar") && window.Type.Contains("Inventory"))
                {
                    Logging.Log("Cleanup", "CloseInventoryWindows: Closing Ship Hangar Window", Logging.white);
                    window.Close();
                    _lastCleanupAction = DateTime.Now;
                    return false;
                }
                if (window.Type.Contains("Inventory"))
                {
                    Logging.Log("Cleanup", "CloseInventoryWindows: Closing other Inventory Window named [ " + window.Name + "]", Logging.white);
                    window.Close();
                    _lastCleanupAction = DateTime.Now;
                    return false;
                }
                // 
                // add ship hangar, items hangar, corp hangar, etc... as at least come of those may be open in space (pos?) or may someday be bugged by ccp. 
                //
            }
            Cache.Instance.NextArmAction = DateTime.Now.AddSeconds(4);
            return true;
        }

        public void CheckEVEStatus()
        {
            // get the current process
            Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();

            // get the physical mem usage (this only runs between missions)
            Cache.Instance.TotalMegaBytesOfMemoryUsed = ((currentProcess.WorkingSet64 / 1024) / 1024);
            Logging.Log("Questor", "EVE instance: totalMegaBytesOfMemoryUsed - " +
                        Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.white);

            // If Questor window not visible, schedule a restart of questor in the uplink so that the GUI will start normally

            /*
             *
             if (!m_Parent.Visible)
            //GUI isn't visible and CloseQuestorflag is true, so that his code block only runs once
            {
                //m_Parent.Visible = true; //this does not work for some reason - innerspace issue?
                Cache.Instance.ReasonToStopQuestor =
                    "The Questor GUI is not visible: did EVE get restarted due to a crash or lag?";
                Logging.Log(Cache.Instance.ReasonToStopQuestor);
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                BeginClosingQuestor();
            }
            else

             */

            if (Cache.Instance.TotalMegaBytesOfMemoryUsed > (Settings.Instance.EVEProcessMemoryCeiling - 50) &&
                        Settings.Instance.EVEProcessMemoryCeilingLogofforExit != "")
            {
                Logging.Log(
                    "Questor", "Memory usage is above the EVEProcessMemoryCeiling threshold. EVE instance: totalMegaBytesOfMemoryUsed - " +
                    Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.white);
                Cache.Instance.ReasonToStopQuestor =
                    "Memory usage is above the EVEProcessMemoryCeiling threshold. EVE instance: totalMegaBytesOfMemoryUsed - " +
                    Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB";
                if (Settings.Instance.EVEProcessMemoryCeilingLogofforExit == "logoff")
                {
                    Cache.Instance.CloseQuestorCMDLogoff = true;
                    Cache.Instance.CloseQuestorCMDExitGame = false;
                    Cache.Instance.SessionState = "LoggingOff";
                    BeginClosingQuestor();
                    return;
                }
                if (Settings.Instance.EVEProcessMemoryCeilingLogofforExit == "exit")
                {
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.SessionState = "Exiting";
                    BeginClosingQuestor();
                    return;
                }
                Logging.Log(
                    "Questor", "EVEProcessMemoryCeilingLogofforExit was not set to exit or logoff - doing nothing ", Logging.red);
            }
            else
            {
                Cache.Instance.SessionState = "Running";
            }
        }

        public void ProcessState()
        {
            if (DateTime.Now < _lastCleanupProcessState.AddMilliseconds(100)) //if it has not been 100ms since the last time we ran this ProcessState return. We can't do anything that close together anyway
                return;

            _lastCleanupProcessState = DateTime.Now;

            // When in warp there's nothing we can do, so ignore everything
            if (Cache.Instance.InWarp)
            {
                if (Settings.Instance.DebugCleanup) Logging.Log("Cleanup", "Processstate: we are in warp: do nothing", Logging.teal);
                _States.CurrentSalvageState = SalvageState.Idle;
                return;
            }

            if (DateTime.Now < Cache.Instance.LastSessionChange.AddSeconds(20))
            {
                if (Settings.Instance.DebugCleanup) Logging.Log("Cleanup", "last session change was at [" + Cache.Instance.LastSessionChange + "] waiting until 20 sec have passed", Logging.teal);
                return;
            }


            if (Cache.Instance.InSpace) 
            {
                if (Settings.Instance.DebugCleanup) Logging.Log("Cleanup", "Processstate: we are in space", Logging.teal);
                if (DateTime.Now < Cache.Instance.LastInStation.AddSeconds(10))
                {
                    if (Settings.Instance.DebugCleanup) Logging.Log("Cleanup", "Processstate: last in station time is [" + Cache.Instance.LastInStation + " waiting until 10 seconds have passed", Logging.teal);
                    return;
                }
            }

            if (Cache.Instance.InStation)
            {
                if (Settings.Instance.DebugCleanup) Logging.Log("Cleanup", "Processstate: we are in station", Logging.teal);
                if (DateTime.Now < Cache.Instance.LastInSpace.AddSeconds(10))
                {
                    if (Settings.Instance.DebugCleanup) Logging.Log("Cleanup", "Processstate: last in space time is [" + Cache.Instance.LastInSpace + " waiting until 10 seconds have passed", Logging.teal);
                    return;
                }
            }

            switch (_States.CurrentCleanupState)
            {
                case CleanupState.Idle:
                    //Cleanup State should only run every 4 seconds
                    if (DateTime.Now.Subtract(_lastCleanupAction).TotalSeconds < 4)
                        return;
                    _States.CurrentCleanupState = CleanupState.CheckModalWindows;
                    break;

                case CleanupState.CheckModalWindows:
                    //
                    // go through *every* window
                    //
                    if (!Cache.Instance.InSpace && !Cache.Instance.InStation)
                    {
                        Logging.Log("Cleanup", "CheckModalWindows: We are in a session change, waiting 4 seconds", Logging.white);
                        _lastCleanupAction = DateTime.Now;
                        _States.CurrentCleanupState = CleanupState.Idle;
                        return;
                    }

                    if (Cache.Instance.Windows == null)
                    {
                        Logging.Log("Cleanup","CheckModalWindows: Cache.intance.windows returned null",Logging.white);
                        _lastCleanupAction = DateTime.Now;
                        _States.CurrentCleanupState = CleanupState.Idle;
                        return;
                    }

                    foreach (DirectWindow window in Cache.Instance.Windows)
                    {
                        // Telecom messages are generally mission info messages: close them
                        if (window.Name == "telecom")
                        {
                            Logging.Log("Cleanup", "Closing telecom message...", Logging.white);
                            Logging.Log("Cleanup", "Content of telecom window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]", Logging.white);
                            window.Close();
                        }

                        // Modal windows must be closed
                        // But lets only close known modal windows
                        if (window.Name == "modal")
                        {
                            bool close = false;
                            bool restart = false;
                            bool restartharsh = false;
                            bool gotobasenow = false;
                            bool sayyes = false;
                            bool sayok = false;
                            bool needhumanintervention = false;

                            //bool sayno = false;
                            if (!string.IsNullOrEmpty(window.Html))
                            {
                                // Server going down /unscheduled/ potentially very soon!
                                // CCP does not reboot in the middle of the day because the server is behaving
                                // dock now to avoid problems
                                gotobasenow |= window.Html.Contains("for a short unscheduled reboot");

                                //errors that are repeatable and unavoidable even after a restart of eve/questor
                                needhumanintervention = window.Html.Contains("Please check your mission journal for further information.");

                                // Server going down
                                close |= window.Html.Contains("Please make sure your characters are out of harm");
                                close |= window.Html.Contains("the servers are down for 30 minutes each day for maintenance and updates");

                                // In space "shit"
                                close |= window.Html.Contains("Item cannot be moved back to a loot container.");
                                close |= window.Html.Contains("you do not have the cargo space");
                                close |= window.Html.Contains("cargo units would be required to complete this operation.");
                                close |= window.Html.Contains("You are too far away from the acceleration gate to activate it!");
                                close |= window.Html.Contains("maximum distance is 2500 meters");
                                close |= window.Html.Contains("you can decline a mission every"); //4 hours without penalty
                                // Stupid warning, lets see if we can find it
                                close |= window.Html.Contains("Do you wish to proceed with this dangerous action?");
                                // Yes we know the mission is not complete, Questor will just redo the mission
                                close |= window.Html.Contains("weapons in that group are already full");
                                close |= window.Html.Contains("You have to be at the drop off location to deliver the items in person");
                                // Lag :/
                                close |= window.Html.Contains("This gate is locked!");
                                close |= window.Html.Contains("The Zbikoki's Hacker Card");
                                close |= window.Html.Contains(" units free.");
                                close |= window.Html.Contains("already full");
                                //windows that can be disabled, but may not yet be disabled 
                                close |= window.Html.Contains("Are you sure you would like to decline this mission");
                                //why are we reloading an already full weapon?
                                close |= window.Html.Contains("All the weapons in this group are already full");
                                //trial account
                                close |= window.Html.Contains("At any time you can log in to the account management page and change your trial account to a paying account");

                                restartharsh |= window.Html.Contains("The connection to the server was closed");
                                restartharsh |= window.Html.Contains("The user's connection has been usurped on the proxy");
                                restartharsh |= window.Html.Contains("The connection to the server was closed"); 										//CONNECTION LOST
                                restartharsh |= window.Html.Contains("server was closed");  															//CONNECTION LOST
                                restartharsh |= window.Html.Contains("The socket was closed"); 															//CONNECTION LOST
                                restartharsh |= window.Html.Contains("The connection was closed"); 														//CONNECTION LOST
                                restartharsh |= window.Html.Contains("Connection to server lost"); 														//CONNECTION LOST
                                restartharsh |= window.Html.Contains("The user connection has been usurped on the proxy"); 								//CONNECTION LOST
                                restartharsh |= window.Html.Contains("The transport has not yet been connected, or authentication was not successful");	//CONNECTION LOST
                                restartharsh |= window.Html.Contains("Your client has waited"); //SOUL-CRUSHING LAG - Your client has waited x minutes for a remote call to complete.
                                restartharsh |= window.Html.Contains("This could mean the server is very loaded"); //SOUL-CRUSHING LAG - Your client has waited x minutes for a remote call to complete.
                                
                                //
                                // restart the client if these are encountered
                                //
                                restart |= window.Html.Contains("Local cache is corrupt");
                                restart |= window.Html.Contains("Local session information is corrupt");
                                //
                                // Modal Dialogs the need "yes" pressed
                                //
                                sayyes |= window.Html.Contains("objectives requiring a total capacity");
                                sayyes |= window.Html.Contains("your ship only has space for");
                                sayyes |= window.Html.Contains("Are you sure you want to remove location");

                                //
                                // LP Store "Accept offer" dialog
                                //
                                sayok |= window.Html.Contains("Are you sure you want to accept this offer?");
                                //
                                // Modal Dialogs the need "no" pressed
                                //
                                //sayno |= window.Html.Contains("Do you wish to proceed with this dangerous action
                            }
                            if (restartharsh)
                            {
                                Logging.Log("Cleanup: RestartWindow", "Restarting eve...", Logging.white);
                                Logging.Log("Cleanup: RestartWindow", "Content of modal window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]", Logging.white);
                                Cache.Instance.CloseQuestorCMDLogoff = false;
                                Cache.Instance.CloseQuestorCMDExitGame = true;
                                Cache.Instance.CloseQuestorEndProcess = true;
                                Cache.Instance.ReasonToStopQuestor = "A message from ccp indicated we were disconnected";
                                Settings.Instance.SecondstoWaitAfterExteringCloseQuestorBeforeExitingEVE = 0;
                                Cache.Instance.SessionState = "Quitting";
                                Cleanup.CloseQuestor();
                                return;
                            }
                            if (restart)
                            {
                                Logging.Log("Cleanup", "Restarting eve...", Logging.white);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]", Logging.white);
                                Cache.Instance.CloseQuestorCMDLogoff = false;
                                Cache.Instance.CloseQuestorCMDExitGame = true;
                                Cache.Instance.CloseQuestorEndProcess = false;
                                Cache.Instance.ReasonToStopQuestor = "A message from ccp indicated we were should restart";
                                Cache.Instance.SessionState = "Quitting";
                                Settings.Instance.SecondstoWaitAfterExteringCloseQuestorBeforeExitingEVE = 30;
                                window.Close();
                                Cleanup.CloseQuestor();
                                return;
                            }
                            if (sayyes)
                            {
                                Logging.Log("Cleanup", "Found a window that needs 'yes' chosen...", Logging.white);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]", Logging.white);
                                window.AnswerModal("Yes");
                                continue;
                            }
                            if (sayok)
                            {
                                Logging.Log("Cleanup", "Saying OK to modal window for lpstore offer.", Logging.white); 
                                window.AnswerModal("OK");
                                continue;
                            }

                            if (close)
                            {
                                Logging.Log("Cleanup", "Closing modal window...", Logging.white);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]", Logging.white);
                                window.Close();
                                continue;
                            }

                            if (gotobasenow)
                            {
                                Logging.Log("Cleanup", "Evidentially the cluster is dieing... and CCP is restarting the server", Logging.white);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]", Logging.white);
                                Cache.Instance.GotoBaseNow = true;
                                Settings.Instance.AutoStart = false;
                                //
                                // do not close eve, let the shutdown of the server do that
                                //
                                //Cache.Instance.CloseQuestorCMDLogoff = false;
                                //Cache.Instance.CloseQuestorCMDExitGame = true;
                                //Cache.Instance.ReasonToStopQuestor = "A message from ccp indicated we were disconnected";
                                //Cache.Instance.SessionState = "Quitting";
                                window.Close();
                                continue;
                            }
                            if (needhumanintervention)
                            {
                                Statistics.Instance.MissionCompletionErrors++;
                                Logging.Log("Cleanup", "This window indicates an error completing a mission: [" + Statistics.Instance.MissionCompletionErrors + "] errors already we will stop questor and halt restarting when we reach 3", Logging.white);
                                window.Close();
                                if (Statistics.Instance.MissionCompletionErrors > 3 && Cache.Instance.InStation)
                                {
                                    if (Cache.Instance.MissionXMLIsAvailable)
                                    {
                                        Logging.Log("Cleanup", "ERROR: Mission XML is available for [" + Cache.Instance.MissionName + "] but we still did not complete the mission after 3 tries! - ERROR!", Logging.white);
                                        Settings.Instance.AutoStart = false;
                                        //we purposely disable autostart so that when we quit eve and questor here it stays closed until manually restarted as this error is fatal (and repeating)
                                        //Cache.Instance.CloseQuestorCMDLogoff = false;
                                        //Cache.Instance.CloseQuestorCMDExitGame = true;
                                        //Cache.Instance.ReasonToStopQuestor = "Could not complete the mission: [" + Cache.Instance.MissionName + "] after [" + Statistics.Instance.MissionCompletionErrors + "] attempts: objective not complete or missing mission completion item or ???";
                                        //Cache.Instance.SessionState = "Exiting";
                                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Error;
                                    }
                                    else
                                    {
                                        Logging.Log("Cleanup", "ERROR: Mission XML is missing for [" + Cache.Instance.MissionName + "] and we we unable to complete the mission after 3 tries! - ERROR!", Logging.white);
                                        Settings.Instance.AutoStart = false; //we purposely disable autostart so that when we quit eve and questor here it stays closed until manually restarted as this error is fatal (and repeating)
                                        //Cache.Instance.CloseQuestorCMDLogoff = false;
                                        //Cache.Instance.CloseQuestorCMDExitGame = true;
                                        //Cache.Instance.ReasonToStopQuestor = "Could not complete the mission: [" + Cache.Instance.MissionName + "] after [" + Statistics.Instance.MissionCompletionErrors + "] attempts: objective not complete or missing mission completion item or ???";
                                        //Cache.Instance.SessionState = "Exiting";
                                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Error;
                                    }
                                }
                                break;
                            }
                        }
                    if (Cache.Instance.InSpace)
                    {
                            if (window.Name.Contains("_ShipDroneBay_") && window.Caption == "Drone Bay")
                            {
                        if (Settings.Instance.UseDrones && 
                           (Cache.Instance.DirectEve.ActiveShip.GroupId != 31 && 
                            Cache.Instance.DirectEve.ActiveShip.GroupId != 28 && 
                            Cache.Instance.DirectEve.ActiveShip.GroupId != 380 &&  
                            _dronebayclosingattempts <= 1))
                        {
                            _lastCleanupAction = DateTime.Now;
                            _dronebayclosingattempts++;
                            // Close the drone bay, its not required in space.
                                    window.Close();
                        }
                    }
                    else
                    {
                        _dronebayclosingattempts = 0;
                    }
                        }
                    }
                    _States.CurrentCleanupState = CleanupState.CheckWindowsThatDontBelongInSpace;
                    break;

                case CleanupState.CheckWindowsThatDontBelongInSpace:

                    _lastCleanupAction = DateTime.Now;
                    _States.CurrentCleanupState = CleanupState.Idle;
                    break;

                default:
                    // Next state
                    _States.CurrentCleanupState = CleanupState.Idle;
                    break;
            }
        }
    }
}