﻿// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

using Questor.Behaviors;

namespace Questor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using DirectEve;
    using System.Linq;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    using global::Questor.Modules.BackgroundTasks;
    using LavishScriptAPI;

    public class Questor
    {
        private readonly QuestorfrmMain m_Parent;
        private readonly LocalWatch _localwatch;
        private readonly Defense _defense;
        private readonly DirectEve _directEve;

        private DateTime _lastPulse;
        private DateTime _lastSalvageTrip = DateTime.MinValue;
        private readonly CombatMissionsBehavior _combatMissionsBehavior;
        private readonly CombatHelperBehavior _combatHelperBehavior;
        private readonly DedicatedBookmarkSalvagerBehavior _dedicatedBookmarkSalvagerBehavior;
        private readonly DirectionalScannerBehavior _directionalScannerBehavior;
        private readonly Cleanup _cleanup;

        public DateTime LastFrame;
        public DateTime LastAction;

        public bool Panicstatereset = false;
        private bool _runOnce30SecAfterStartupalreadyProcessed = false;

        private readonly Stopwatch _watch;

        public Questor(QuestorfrmMain form1)
        {
            m_Parent = form1;
            _lastPulse = DateTime.MinValue;

            _defense = new Defense();
            _localwatch = new LocalWatch();
            _combatMissionsBehavior = new CombatMissionsBehavior();
            _combatHelperBehavior = new CombatHelperBehavior();
            _dedicatedBookmarkSalvagerBehavior = new DedicatedBookmarkSalvagerBehavior();
            _directionalScannerBehavior = new DirectionalScannerBehavior();
            _cleanup = new Cleanup();
            _watch = new Stopwatch();

            // State fixed on ExecuteMission
            _States.CurrentQuestorState = QuestorState.Idle;

            _directEve = new DirectEve();
            Cache.Instance.DirectEve = _directEve;

            Cache.Instance.StopTimeSpecified = Program.StopTimeSpecified;
            Cache.Instance.MaxRuntime = Program.MaxRuntime;
            Cache.Instance.StopTime = Program.StopTime;
            Cache.Instance.StartTime = Program.startTime;
            Cache.Instance.QuestorStarted_DateTime = DateTime.Now;

            // get the current process
            Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            // get the physical mem usage
            Cache.Instance.TotalMegaBytesOfMemoryUsed = ((currentProcess.WorkingSet64 / 1024) / 1024);
            Logging.Log("Questor", "EVE instance: totalMegaBytesOfMemoryUsed - " +
                        Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.white);
            Cache.Instance.SessionIskGenerated = 0;
            Cache.Instance.SessionLootGenerated = 0;
            Cache.Instance.SessionLPGenerated = 0;
            Settings.Instance.CharacterMode = "none";
            _directEve.OnFrame += OnFrame;
        }

        public string CharacterName { get; set; }

        public static long AgentID;

        public void DebugCombatMissionsBehaviorStates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("CombatMissionsBehavior.State is", _States.CurrentQuestorState.ToString(), Logging.white);
        }

        //public void DebugPanicstates()
        //{
        //    if (Settings.Instance.DebugStates)
        //        Logging.Log("Panic.State = " + _panic.State);
        //    }

        public void RunOnce30SecAfterStartup()
        {
            if (!_runOnce30SecAfterStartupalreadyProcessed && DateTime.Now > Cache.Instance.QuestorStarted_DateTime.AddSeconds(30))
            {
                if (Settings.Instance.CharacterName != null && DateTime.Now > Cache.Instance.NextStartupAction)
                {
                    _runOnce30SecAfterStartupalreadyProcessed = true;
                    if (Settings.Instance.UseInnerspace)
                    {
                        //enable windowtaskbar = on, so that minimized windows do not make us die in a fire.
                        Logging.Log("Questor", "Running Innerspace command: windowtaskbar on" + Settings.Instance.CharacterName, Logging.white);
                        LavishScript.ExecuteCommand("windowtaskbar on" + Settings.Instance.CharacterName);
                    }
                }
                else
                {
                    Logging.Log("Questor", "RunOnce30SecAfterStartup: Settings.Instance.CharacterName is still null", Logging.orange);
                    Cache.Instance.NextStartupAction = DateTime.Now.AddSeconds(30);
                }
            }
        }

        public void DebugPerformanceClearandStartTimer()
        {
            _watch.Reset();
            _watch.Start();
        }

        public void DebugPerformanceStopandDisplayTimer(string whatWeAreTiming)
        {
            _watch.Stop();
            if (Settings.Instance.DebugPerformance)
                Logging.Log(whatWeAreTiming, " took " + _watch.ElapsedMilliseconds + "ms", Logging.white);
        }

        public static void BeginClosingQuestor()
        {
            Cache.Instance.EnteredCloseQuestor_DateTime = DateTime.Now;
            Cache.Instance.SessionState = "Quitting";
            _States.CurrentQuestorState = QuestorState.CloseQuestor;
            return;
        }

        public static void TimeCheck()
        {
            Cache.Instance.LastTimeCheckAction = DateTime.Now;
            if (DateTime.Now.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes >
                Cache.Instance.MaxRuntime)
            {
                // quit questor
                Logging.Log("Questor", "Maximum runtime exceeded.  Quiting...", Logging.white);
                Cache.Instance.ReasonToStopQuestor = "Maximum runtime specified and reached.";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                BeginClosingQuestor();
                return;
            }
            if (Cache.Instance.StopTimeSpecified)
            {
                if (DateTime.Now >= Cache.Instance.StopTime)
                {
                    Logging.Log("Questor", "Time to stop.  Quitting game.", Logging.white);
                    Cache.Instance.ReasonToStopQuestor = "StopTimeSpecified and reached.";
                    Settings.Instance.AutoStart = false;
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.SessionState = "Exiting";
                    BeginClosingQuestor();
                    return;
                }
            }
            if (Cache.Instance.ExitWhenIdle)
            {
                Logging.Log("Questor", "ExitWhenIdle set to true.  Quitting game.", Logging.white);
                Cache.Instance.ReasonToStopQuestor = "ExitWhenIdle se to true";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                BeginClosingQuestor();
                return;
            }
        }

        public static void WalletCheck()
        {
            Cache.Instance.LastWalletCheck = DateTime.Now;
            //Logging.Log("[Questor] Wallet Balance Debug Info: lastknowngoodconnectedtime = " + Settings.Instance.lastKnownGoodConnectedTime);
            //Logging.Log("[Questor] Wallet Balance Debug Info: DateTime.Now - lastknowngoodconnectedtime = " + DateTime.Now.Subtract(Settings.Instance.lastKnownGoodConnectedTime).TotalSeconds);
            if (Math.Round(DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes) > 1)
            {
                Logging.Log("Questor", String.Format("Wallet Balance Has Not Changed in [ {0} ] minutes.",
                                          Math.Round(
                                              DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).
                                                  TotalMinutes, 0)), Logging.white);
            }

            //Settings.Instance.walletbalancechangelogoffdelay = 2;  //used for debugging purposes
            //Logging.Log("Cache.Instance.lastKnownGoodConnectedTime is currently: " + Cache.Instance.lastKnownGoodConnectedTime);
            if (Math.Round(DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes) < Settings.Instance.Walletbalancechangelogoffdelay)
            {
                if ((long)Cache.Instance.MyWalletBalance != (long)Cache.Instance.DirectEve.Me.Wealth)
                {
                    Cache.Instance.LastKnownGoodConnectedTime = DateTime.Now;
                    Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                }
            }
            else if (Settings.Instance.Walletbalancechangelogoffdelay != 0)
            {
                Logging.Log(
                    "Questor", String.Format(
                        "Questor: Wallet Balance Has Not Changed in [ {0} ] minutes. Switching to QuestorState.CloseQuestor",
                        Math.Round(
                            DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes, 0)), Logging.white);
                Cache.Instance.ReasonToStopQuestor = "Wallet Balance did not change for over " +
                                                     Settings.Instance.Walletbalancechangelogoffdelay + "min";

                if (Settings.Instance.WalletbalancechangelogoffdelayLogofforExit == "logoff")
                {
                    Logging.Log("Questor", "walletbalancechangelogoffdelayLogofforExit is set to: " +
                                Settings.Instance.WalletbalancechangelogoffdelayLogofforExit, Logging.white);
                    Cache.Instance.CloseQuestorCMDLogoff = true;
                    Cache.Instance.CloseQuestorCMDExitGame = false;
                    Cache.Instance.SessionState = "LoggingOff";
                }
                if (Settings.Instance.WalletbalancechangelogoffdelayLogofforExit == "exit")
                {
                    Logging.Log("Questor", "walletbalancechangelogoffdelayLogofforExit is set to: " +
                                Settings.Instance.WalletbalancechangelogoffdelayLogofforExit, Logging.white);
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.SessionState = "Exiting";
                }
                BeginClosingQuestor();
                return;
            }
        }
        public static void CheckEVEStatus()
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
                Logging.Log("ReasonToStopQuestor" + Cache.Instance.ReasonToStopQuestor);
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
                    "Questor", ": Memory usage is above the EVEProcessMemoryCeiling threshold. EVE instance: totalMegaBytesOfMemoryUsed - " +
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
                    "Questor", "EVEProcessMemoryCeilingLogofforExit was not set to exit or logoff - doing nothing ", Logging.white);
            }
            else
            {
                Cache.Instance.SessionState = "Running";
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            var watch = new Stopwatch();
            Cache.Instance.LastFrame = DateTime.Now;

            // Only pulse state changes every 1.5s
            if (DateTime.Now.Subtract(_lastPulse).TotalMilliseconds < (int)Time.QuestorPulse_milliseconds) //default: 1500ms
                return;
            _lastPulse = DateTime.Now;

            if (DateTime.Now < Cache.Instance.QuestorStarted_DateTime.AddSeconds(30))
            {
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.Now;
            }

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

            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            // Update settings (settings only load if character name changed)
            if (!Settings.Instance.Defaultsettingsloaded)
            {
                Settings.Instance.LoadSettings();
            }
            CharacterName = Cache.Instance.DirectEve.Me.Name;

            // Check 3D rendering
            if (Cache.Instance.DirectEve.Session.IsInSpace &&
                Cache.Instance.DirectEve.Rendering3D != !Settings.Instance.Disable3D)
                Cache.Instance.DirectEve.Rendering3D = !Settings.Instance.Disable3D;

            if (DateTime.Now.Subtract(Cache.Instance.LastupdateofSessionRunningTime).TotalSeconds <
                (int)Time.SessionRunningTimeUpdate_seconds)
            {
                Cache.Instance.SessionRunningTime =
                    (int)DateTime.Now.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes;
                Cache.Instance.LastupdateofSessionRunningTime = DateTime.Now;
            }

            if (!Cache.Instance.Paused)
            {
                if (DateTime.Now.Subtract(Cache.Instance.LastWalletCheck).TotalMinutes > (int)Time.WalletCheck_minutes && !Settings.Instance.Defaultsettingsloaded)
                {
                    WalletCheck();
                }
            }

            // We always check our defense state if we're in space, regardless of questor state
            // We also always check panic
            if (Cache.Instance.InSpace)
            {
                DebugPerformanceClearandStartTimer();
                if (!Cache.Instance.DoNotBreakInvul)
                {
                    _defense.ProcessState();
                }
                DebugPerformanceStopandDisplayTimer("Defense.ProcessState");
            }

            if (Cache.Instance.Paused)
            {
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.Now;
                Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                Cache.Instance.GotoBaseNow = false;
                Cache.Instance.SessionState = string.Empty;
                return;
            }

            if (Cache.Instance.SessionState == "Quitting")
            {
                if (_States.CurrentQuestorState != QuestorState.CloseQuestor)
                {
                    BeginClosingQuestor();
                }
            }

            // Start _cleanup.ProcessState
            // Description: Closes Windows, and eventually other things considered 'cleanup' useful to more than just Questor(Missions) but also Anomalies, Mining, etc
            //
            DebugPerformanceClearandStartTimer();
            _cleanup.ProcessState();
            DebugPerformanceStopandDisplayTimer("Cleanup.ProcessState");

            if (Settings.Instance.DebugStates)
                Logging.Log("Cleanup.State is", _States.CurrentCleanupState.ToString(), Logging.white);

            // Done
            // Cleanup State: ProcessState

            // When in warp there's nothing we can do, so ignore everything
            if (Cache.Instance.InWarp)
                return;

            //DirectAgentMission mission;
            switch (_States.CurrentQuestorState)
            {
                case QuestorState.Idle:
                    // Every 5 min of idle check and make sure we aren't supposed to stop...
                    if (Math.Round(DateTime.Now.Subtract(Cache.Instance.LastTimeCheckAction).TotalMinutes) > 5)
                    {
                        TimeCheck(); //Should we close questor due to stoptime or runtime?
                    }
                    if (Cache.Instance.StopBot)
                        return;

                    if (_States.CurrentQuestorState == QuestorState.Idle && Settings.Instance.CharacterMode != "none")
                    {
                        _States.CurrentQuestorState = QuestorState.Start;
                        return;
                    }
                    break;

                case QuestorState.CombatMissionsBehavior:
                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Idle)
                    {
                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Idle;
                    }
                    _combatMissionsBehavior.ProcessState();
                    break;

                case QuestorState.CombatHelperBehavior:
                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.Idle)
                    {
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                    }
                    _combatHelperBehavior.ProcessState();
                    break;

                case QuestorState.DedicatedBookmarkSalvagerBehavior:
                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.Idle)
                    {
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
                    }
                    _dedicatedBookmarkSalvagerBehavior.ProcessState();
                    break;

                case QuestorState.DirectionalScannerBehavior:
                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.Idle)
                    {
                        _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Idle;
                    }
                    _directionalScannerBehavior.ProcessState();
                    break;

                case QuestorState.Start:
                    switch (Settings.Instance.CharacterMode.ToLower())
                    {
                        case "combat missions":
                        case "combat_missions":
                        case "dps":
                            Logging.Log("Questor", "Start Mission Behavior", Logging.white);
                            _States.CurrentQuestorState = QuestorState.CombatMissionsBehavior;
                            break;

                        case "salvage":
                            Logging.Log("Questor", "Start Salvaging Behavior", Logging.white);
                            _States.CurrentQuestorState = QuestorState.DedicatedBookmarkSalvagerBehavior;
                            break;

                        case "combat helper":
                        case "combat_helper":
                        case "combathelper":
                            Logging.Log("Questor", "Start CombatHelper Behavior", Logging.white);
                            _States.CurrentQuestorState = QuestorState.CombatHelperBehavior;
                            break;

                        case "directionalscanner":
                            Logging.Log("Questor", "Start DirectionalScanner Behavior", Logging.white);
                            _States.CurrentQuestorState = QuestorState.DirectionalScannerBehavior;
                            break;
                    }
                    break;

                case QuestorState.CloseQuestor:
                    Cleanup.CloseQuestor();
                    return;

                case QuestorState.DebugCloseQuestor:
                    //Logging.Log("ISBoxerCharacterSet: " + Settings.Instance.Lavish_ISBoxerCharacterSet);
                    //Logging.Log("Profile: " + Settings.Instance.Lavish_InnerspaceProfile);
                    //Logging.Log("Game: " + Settings.Instance.Lavish_Game);
                    Logging.Log("Questor", "CloseQuestorCMDUplinkInnerspaceProfile: " + Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile, Logging.white);
                    Logging.Log("Questor", "CloseQuestorCMDUplinkISboxerCharacterSet: " + Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet, Logging.white);
                    Logging.Log("Questor", "CloseQuestorArbitraryOSCmd" + Settings.Instance.CloseQuestorArbitraryOSCmd, Logging.white);
                    Logging.Log("Questor", "CloseQuestorOSCmdContents" + Settings.Instance.CloseQuestorOSCmdContents, Logging.white);
                    Logging.Log("Questor", "walletbalancechangelogoffdelay: " + Settings.Instance.Walletbalancechangelogoffdelay, Logging.white);
                    Logging.Log("Questor", "walletbalancechangelogoffdelayLogofforExit: " + Settings.Instance.WalletbalancechangelogoffdelayLogofforExit, Logging.white);
                    Logging.Log("Questor", "EVEProcessMemoryCeiling: " + Settings.Instance.EVEProcessMemoryCeiling, Logging.white);
                    Logging.Log("Questor", "EVEProcessMemoryCielingLogofforExit: " + Settings.Instance.EVEProcessMemoryCeilingLogofforExit, Logging.white);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorCMDExitGame: " + Cache.Instance.CloseQuestorCMDExitGame, Logging.white);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorCMDLogoff: " + Cache.Instance.CloseQuestorCMDLogoff, Logging.white);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorEndProcess: " + Cache.Instance.CloseQuestorEndProcess, Logging.white);
                    Logging.Log("Questor", "Cache.Instance.EnteredCloseQuestor_DateTime: " + Cache.Instance.EnteredCloseQuestor_DateTime.ToShortTimeString(), Logging.white);
                    _States.CurrentQuestorState = QuestorState.Error;
                    return;

                case QuestorState.DebugWindows:
                    List<DirectWindow> windows = Cache.Instance.Windows;

                    foreach (DirectWindow window in windows)
                    {
                        Logging.Log("Questor", "--------------------------------------------------", Logging.orange);
                        Logging.Log("Questor", "Debug_Window.Name: [" + window.Name + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.Caption: [" + window.Caption + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.Type: [" + window.Type + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.IsModal: [" + window.IsModal + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.IsDialog: [" + window.IsDialog + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.Id: [" + window.Id + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.IsKillable: [" + window.IsKillable + "]", Logging.white);
                        //Logging.Log("Questor", "Debug_Window.Html: [" + window.Html + "]", Logging.white);
                    }
                    Logging.Log("Questor", "Debug_InventoryWindows", Logging.white);
                    foreach (DirectWindow window in windows)
                    {
                        if (window.Type.Contains("inventory"))
                        {
                            Logging.Log("Questor", "Debug_Window.Name: [" + window.Name + "]", Logging.white);
                            Logging.Log("Questor", "Debug_Window.Type: [" + window.Type + "]", Logging.white);
                            Logging.Log("Questor", "Debug_Window.Caption: [" + window.Caption + "]", Logging.white);
                            //Logging.Log("Questor", "Debug_Window.Type: [" + window. + "]", Logging.white);
                        }
                    }
                    _States.CurrentQuestorState = QuestorState.Error;
                    return;

                case QuestorState.DebugInventoryTree:
                    
                    if (Cache.Instance.InventoryWindow.ExpandCorpHangarView())
                    {
                        Logging.Log("DebugInventoryTree", "ExpandCorpHangar executed", Logging.teal);
                    }
                    Logging.Log("DebugInventoryTree", "--------------------------------------------------", Logging.orange);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Name: [" + Cache.Instance.InventoryWindow.Name + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Caption: [" + Cache.Instance.InventoryWindow.Caption + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Type: [" + Cache.Instance.InventoryWindow.Type + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsModal: [" + Cache.Instance.InventoryWindow.IsModal + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsDialog: [" + Cache.Instance.InventoryWindow.IsDialog + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Id: [" + Cache.Instance.InventoryWindow.Id + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsKillable: [" + Cache.Instance.InventoryWindow.IsKillable + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsReady: [" + Cache.Instance.InventoryWindow.IsReady + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.LocationFlag: [" + Cache.Instance.InventoryWindow.LocationFlag + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.currInvIdName: " + Cache.Instance.InventoryWindow.currInvIdName, Logging.red);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.currInvIdName: " + Cache.Instance.InventoryWindow.currInvIdItem, Logging.red);

                    foreach (Int64 itemintree in Cache.Instance.IDsinInventoryTree)
                    {
                        if (Cache.Instance.InventoryWindow.GetIdsFromTree(false).Contains(itemintree))
                        {
                            Cache.Instance.InventoryWindow.SelectTreeEntryByID(itemintree);
                            Cache.Instance.IDsinInventoryTree.Remove(itemintree);
                            break;
                        }
                    }
                    break;
            }
        }
    }
}