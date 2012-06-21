
using System.Diagnostics;

namespace Questor.Modules.BackgroundTasks
{
    using System;

    //using System.Linq;
    using DirectEve;
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

        private void BeginClosingQuestor()
        {
            Cache.Instance.EnteredCloseQuestor_DateTime = DateTime.Now;
            Cache.Instance.SessionState = "Quitting";
        }

        public static bool CloseInventoryWindows()
        {
            if (DateTime.Now < _lastCleanupAction.AddMilliseconds(500))
                return false;
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
                _States.CurrentSalvageState = SalvageState.Idle;
                return;
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
                                //
                                // restart the client if these are encountered
                                //
                                restart |= window.Html.Contains("Local cache is corrupt");
                                restart |= window.Html.Contains("Local session information is corrupt");
                                restart |= window.Html.Contains("The connection to the server was closed"); 										//CONNECTION LOST
                                restart |= window.Html.Contains("server was closed");  																//CONNECTION LOST
                                restart |= window.Html.Contains("The socket was closed"); 															//CONNECTION LOST
                                restart |= window.Html.Contains("The connection was closed"); 														//CONNECTION LOST
                                restart |= window.Html.Contains("Connection to server lost"); 														//INFORMATION
                                restart |= window.Html.Contains("The user connection has been usurped on the proxy"); 								//CONNECTION LOST
                                restart |= window.Html.Contains("The transport has not yet been connected, or authentication was not successful"); 	//CONNECTION LOST
                                restart |= window.Html.Contains("Your client has waited"); //SOUL-CRUSHING LAG - Your client has waited x minutes for a remote call to complete.
                                restart |= window.Html.Contains("This could mean the server is very loaded"); //SOUL-CRUSHING LAG - Your client has waited x minutes for a remote call to complete.
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

                            if (restart)
                            {
                                Logging.Log("Cleanup", "Restarting eve...", Logging.white);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]", Logging.white);
                                Cache.Instance.CloseQuestorCMDLogoff = false;
                                Cache.Instance.CloseQuestorCMDExitGame = true;
                                Cache.Instance.ReasonToStopQuestor = "A message from ccp indicated we were disconnected";
                                Cache.Instance.SessionState = "Quitting";
                                Settings.Instance.SecondstoWaitAfterExteringCloseQuestorBeforeExitingEVE = 30;
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