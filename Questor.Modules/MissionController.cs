﻿// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
// 
//     Please look in the accompanying license.htm file for the license that 
//     applies to this source code. (a copy can also be found at: 
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------
namespace Questor.Modules
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using DirectEve;

    public class MissionController
    {
        private DateTime? _clearPocketTimeout;
        private int _currentAction;
        private DateTime _lastActivateAction;
        private DateTime _lastApproachAction;
        private DateTime _lastBookmarkPocketAttempt;
        private readonly Dictionary<long, DateTime> _lastWeaponReload = new Dictionary<long, DateTime>();
        private double _lastX;
        private double _lastY;
        private double _lastZ;
        private int _pocket;
        private List<Action> _pocketActions;
        private bool _waiting;
        private DateTime _waitingSince;
        private DateTime _lastAlign;
        private DateTime _lastOrbit;
        private DateTime _lastReload;

        private bool target_null = false;
        public long AgentId { get; set; }

        public MissionController()
        {
            _pocketActions = new List<Action>();
        }

        public MissionControllerState State { get; set; }
        // Statistics information
        //public DateTime Started { get; set; }
        public DateTime StartedPocket { get; set; }
        public string Mission { get; set; }
        public double Wealth { get; set; }
        public double LootValue { get; set; }
        public int LoyaltyPoints { get; set; }
        public int LostDrones { get; set; }
        
        private void LogStatistics()
        {
            // We arent suppose to create bookmarks
            //if (!Settings.Instance.LogBounties)
            //    return;
            var mission = Cache.Instance.GetAgentMission(AgentId);
            var currentPocketName = Cache.Instance.FilterPath(mission.Name);
            if (Settings.Instance.PocketStatistics)
            {
                Settings.Instance.PocketStatisticsFile = Path.Combine(Settings.Instance.PocketStatisticsPath, Cache.Instance.FilterPath(Cache.Instance.DirectEve.Me.Name) + " - " + currentPocketName + " - " + _pocket + " - PocketStatistics.csv");

                if (!Directory.Exists(Settings.Instance.PocketStatisticsPath)) 
                    Directory.CreateDirectory(Settings.Instance.PocketStatisticsPath);

                //
                // this is writing down stats from the PREVIOUS pocket (if any?!)
                //

                // Write the header
                if (!File.Exists(Settings.Instance.PocketStatisticsFile))
                    File.AppendAllText(Settings.Instance.PocketStatisticsFile, "Date and Time;Mission Name and Pocket;Time to complete;Isk;panics;LowestShields;LowestArmor;LowestCapacitor;RepairCycles\r\n");

                // Build the line
                var pocketstats_line = DateTime.Now + ";";                                                 //Date
                pocketstats_line += currentPocketName + ";" + "pocket" + (_pocket) + ";";                  //Mission Name and Pocket
                pocketstats_line += ((int)DateTime.Now.Subtract(StartedPocket).TotalMinutes) + ";";        //Time to Complete
                pocketstats_line += ((long)(Cache.Instance.DirectEve.Me.Wealth - Wealth)) + ";";           //Isk
                pocketstats_line += ((int)Cache.Instance.panic_attempts_this_pocket) + ";";                //Panics
                pocketstats_line += ((int)Cache.Instance.lowest_shield_percentage_this_pocket) + ";";      //LowestShields
                pocketstats_line += ((int)Cache.Instance.lowest_armor_percentage_this_pocket) + ";";       //LowestArmor
                pocketstats_line += ((int)Cache.Instance.lowest_capacitor_percentage_this_pocket) + ";";   //LowestCapacitor
                pocketstats_line += ((int)Cache.Instance.repair_cycle_time_this_pocket) + ";\r\n";         //repairCycles

                // The old pocket is finished
                Logging.Log("MissionController: Writing pocket statistics to [ " + Settings.Instance.PocketStatisticsFile + "and clearing stats for next pocket");
                File.AppendAllText(Settings.Instance.PocketStatisticsFile, pocketstats_line);
            }
            // Update statistic values for next pocket stats
            Wealth = Cache.Instance.DirectEve.Me.Wealth;
            StartedPocket = DateTime.Now;
            Cache.Instance.panic_attempts_this_pocket = 0;
            Cache.Instance.lowest_shield_percentage_this_pocket = 101;
            Cache.Instance.lowest_armor_percentage_this_pocket = 101;
            Cache.Instance.lowest_capacitor_percentage_this_pocket = 101;
            Cache.Instance.repair_cycle_time_this_pocket = 0;
            LostDrones = 0;
        }

        private void ReloadAll()
        {
            var weapons = Cache.Instance.Weapons;
            var cargo = Cache.Instance.DirectEve.GetShipsCargo();
            var correctAmmo1 = Settings.Instance.Ammo.Where(a => a.DamageType == Cache.Instance.DamageType);

            correctAmmo1 = correctAmmo1.Where(a => cargo.Items.Any(i => i.TypeId == a.TypeId));

            if (correctAmmo1.Count() == 0)
                return;

            var ammo = correctAmmo1.Where(a => a.Range > 1).OrderBy(a => a.Range).FirstOrDefault();
            var charge = cargo.Items.FirstOrDefault(i => i.TypeId == ammo.TypeId);

            if (ammo == null)
                return;

            Cache.Instance.TimeSpentReloading_seconds = Cache.Instance.TimeSpentReloading_seconds + (int)Time.ReloadWeaponDelayBeforeUsable_seconds;

            foreach (var weapon in weapons)
            {
                // Reloading energy weapons prematurely just results in unnecessary error messages, so let's not do that
                if (weapon.IsEnergyWeapon)
                    return;

                if (weapon.CurrentCharges >= weapon.MaxCharges)
                    return;

                if (weapon.IsReloadingAmmo || weapon.IsDeactivating || weapon.IsChangingAmmo)
                    return;

                if (_lastWeaponReload.ContainsKey(weapon.ItemId) && DateTime.Now < _lastWeaponReload[weapon.ItemId].AddSeconds((int)Time.ReloadWeaponDelayBeforeUsable_seconds))
                    return;
                _lastWeaponReload[weapon.ItemId] = DateTime.Now;

                if (weapon.Charge.TypeId == charge.TypeId)
                {
                    Logging.Log("MissionController: ReloadingAll [" + weapon.ItemId + "] with [" + charge.TypeName + "][" + charge.TypeId + "]");

                    weapon.ReloadAmmo(charge);
                }

            }
            return;
        }

        private void BookmarkPocketForSalvaging()
        {
            // Nothing to loot
            if (Cache.Instance.UnlootedContainers.Count() < Settings.Instance.MinimumWreckCount)
            {
                // If Settings.Instance.LootEverything is false we may leave behind a lot of unlooted containers.
                // This scenario only happens when all wrecks are within tractor range and you have a salvager 
                // (typically only with a Golem).  Check to see if there are any cargo containers in space.  Cap 
                // boosters may cause an unneeded salvage trip but that is better than leaving millions in loot behind.  
                if (DateTime.Now.Subtract(_lastBookmarkPocketAttempt).TotalSeconds > (int)Time.BookmarkPocketRetryDelay_seconds)
                {
                    if (!Settings.Instance.LootEverything && Cache.Instance.Containers.Count() < Settings.Instance.MinimumWreckCount)
                    {
                        Logging.Log("MissionController: No bookmark created because the pocket has [" + Cache.Instance.Containers.Count() + "] wrecks/containers and the minimum is [" + Settings.Instance.MinimumWreckCount + "]");
                        _lastBookmarkPocketAttempt = DateTime.Now;
                    }
                    else if (Settings.Instance.LootEverything)
                    {
                        Logging.Log("MissionController: No bookmark created because the pocket has [" + Cache.Instance.UnlootedContainers.Count() + "] wrecks/containers and the minimum is [" + Settings.Instance.MinimumWreckCount + "]");
                        _lastBookmarkPocketAttempt = DateTime.Now;
                    }
                }

            }
            else
            {
                // Do we already have a bookmark?
                var bookmarks = Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ");
                var bookmark = bookmarks.FirstOrDefault(b => Cache.Instance.DistanceFromMe(b.X ?? 0, b.Y ?? 0, b.Z ?? 0) < (int)Distance.BookmarksOnGridWithMe);
                if (bookmark != null)
                {
                    Logging.Log("MissionController: Pocket already bookmarked for salvaging [" + bookmark.Title + "]");
                }
                else
                {
                    // No, create a bookmark
                    var label = string.Format("{0} {1:HHmm}", Settings.Instance.BookmarkPrefix, DateTime.UtcNow);
                    //var containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderBy(e => e.Distance);
                    Logging.Log("MissionController: Bookmarking pocket for salvaging [" + label + "]");
                    Cache.Instance.CreateBookmark(label);
                    //Cache.Instance.CreateBookmarkofwreck(containers,label);
                }
            }
        }

        private void ActivateAction(Action action)
        {
            var target = action.GetParameterValue("target");

            // No parameter? Although we shouldnt really allow it, assume its the acceleration gate :)
            if (string.IsNullOrEmpty(target))
                target = "Acceleration Gate";

            var targets = Cache.Instance.EntitiesByName(target);
            if (targets == null || targets.Count() == 0)
            {
                if (!_waiting)
                {
                    Logging.Log("MissionController.Activate: Can't find [" + target + "] to activate! Waiting 30 seconds before giving up");
                    _waitingSince = DateTime.Now;
                    _waiting = true;
                }
                else if (_waiting)
                {
                    if (DateTime.Now.Subtract(_waitingSince).TotalSeconds > (int)Time.ActivateAction_NoGateFound_delay)
                    {
                        Logging.Log("MissionController.Activate: After 30 seconds of waiting the gate is still not on grid: MissionControllerState.Error");
                        State = MissionControllerState.Error;
                    }
                }
                return;
            }
            
            //if (closest.Distance <= (int)Distance.CloseToGateActivationRange) // if your distance is less than the 'close enough' range, default is 7000 meters
            var closest = targets.OrderBy(t => t.Distance).First();
            if (closest.Distance < (int)Distance.GateActivationRange)
            {
                // Tell the drones module to retract drones
                Cache.Instance.IsMissionPocketDone = true;

                // We cant activate if we have drones out
                if (Cache.Instance.ActiveDrones.Count() > 0)
                    return;

                //
                // this is a bad idea for a speed tank, we ought to somehow cache the object they are orbiting/approaching, etc
                // this seemingly slowed down the exit from cetain missions for me for 2-3min as it had a command to orbit some random object
                // after the "done" command
                //
                if (closest.Distance < -10100)
                {
                    if (DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 15)
                    {
                        closest.Orbit(1000);
                        Logging.Log("MissionController: Activate: We are too close to [" + closest.Name + "] Initiating orbit");
                        _lastOrbit = DateTime.Now;
                    }
                }
                //Logging.Log("MissionController: distance " + closest.Distance);
                //if ((closest.Distance <= (int)Distance.TooCloseToStructure) && (DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 30)) //-10100 meters (inside docking ring) - so close that we may get tangled in the structure on activation - move away
                //{
                //    Logging.Log("MissionController.Activate: Too close to Structure to activate: orbiting");
                //    closest.Orbit((int)Distance.GateActivationRange); // 1000 meters
                //    _lastOrbit = DateTime.Now;
                //}

                //if (closest.Distance >= (int)Distance.TooCloseToStructure) //If we aren't so close that we may get tangled in the structure, activate it
                if (closest.Distance >= -10100)
                {
                    // Add bookmark (before we activate)
                    if (Settings.Instance.CreateSalvageBookmarks)
                        BookmarkPocketForSalvaging();

                    // Reload weapons and activate gate to move to the next pocket
                    if (DateTime.Now.Subtract(_lastReload).TotalSeconds > 20)
                    {
                        Logging.Log("MissionController: ReloadALL: Reload before moving to next pocket");
                        ReloadAll();
                        _lastReload = DateTime.Now;
                    }
                    Logging.Log("MissionController: closest.Activate: [" + closest.Name + "] Move to next pocket after reload command and change state to 'NextPocket'");
                    closest.Activate();

                    // Do not change actions, if NextPocket gets a timeout (>2 mins) then it reverts to the last action
                    

                    _lastActivateAction = DateTime.Now;
                    State = MissionControllerState.NextPocket;
                }
            }
            else if (closest.Distance < (int)Distance.WarptoDistance) //else if (closest.Distance < (int)Distance.WarptoDistance) //if we are inside warpto distance then approach
            {
                // Move to the target
                if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id)
                {
                    if (DateTime.Now.Subtract(_lastApproachAction).TotalSeconds > 2)
                    {
                    Logging.Log("MissionController.Activate: Approaching target [" + closest.Name + "][" + closest.Id + "]");
                        _lastApproachAction = DateTime.Now;
                    closest.Approach();
                    }
                    
                }
            }
            else //we must be outside warpto distance, but we are likley in a deadspace so align to the target
            {
                // We cant warp if we have drones out
                if (Cache.Instance.ActiveDrones.Count() > 0)
                    return;
                    
                if (DateTime.Now.Subtract(_lastAlign ).TotalMinutes > (int)Time.LastAlignDelay_minutes)
                {
                    // Only happens if we are asked to Activate something that is outside Distance.CloseToGateActivationRange (default is: 6k)
                    Logging.Log("MissionController: closest.AlignTo: [" + closest.Name + "] This only happens if we are asked to Activate something that is outside [" + Distance.CloseToGateActivationRange + "]");
                    closest.AlignTo();
                    _lastAlign = DateTime.Now;
                }
            }
        }

        private void ClearPocketAction(Action action)
        {
            if (!Cache.Instance.NormalApproch)
                Cache.Instance.NormalApproch = true;
            
            // Get lowest range
            var range = Math.Min(Cache.Instance.WeaponRange, Cache.Instance.DirectEve.ActiveShip.MaxTargetRange);

            // Is there a priority target out of range?
            var target = Cache.Instance.PriorityTargets.OrderBy(t => t.Distance).Where(t => !(Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()) && !Cache.Instance.TargetedBy.Any(w => w.IsWarpScramblingMe || w.IsNeutralizingMe || w.IsWebbingMe))).FirstOrDefault();
            if (target == null)
                target_null = true;
            else
                target_null = false;
            // Or is there a target out of range that is targeting us?
            target = target ?? Cache.Instance.TargetedBy.Where(t => !t.IsSentry && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();
            // Or is there any target out of range?
            target = target ?? Cache.Instance.Entities.Where(t => !t.IsSentry && !t.IsContainer && t.IsNpc && t.CategoryId == (int) CategoryID.Entity && t.GroupId != (int) Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();
            int targetedby = Cache.Instance.TargetedBy.Where(t => !t.IsSentry && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).Count();

            if (target != null)
            {
                // Reset timeout
                _clearPocketTimeout = null;

                // Lock priority target if within weapons range
                if (target.Distance < range)
                {
                    if (target_null && targetedby == 0 && DateTime.Now.Subtract(_lastReload).TotalSeconds > 20)
                    {
                        Logging.Log("MissionController: ReloadALL: Reload if [" + target_null + "] && [" + targetedby + "] == 0 AND [" + target.Distance + "] < [" + range + "]");
                        ReloadAll();
                        _lastReload = DateTime.Now;
                    }

                    if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                    {
                        if (target.IsTarget) //This target is already targeted no need to target it again
                        {
                            return;
                        }
                        else
                        {
                            Logging.Log("MissionController.ClearPocket: Targeting [" + target.Name + "][" + target.Id + "] - Distance [" + target.Distance + "]");
                            target.LockTarget();
                        }
                    }
                    return;
                }
                else
                {
                    if (DateTime.Now.Subtract(_lastReload).TotalSeconds > 20)
                    {
                        Logging.Log("MissionController: ReloadAll: Reload weapons");
                        ReloadAll();
                        _lastReload = DateTime.Now;
                    }
                }

                // Are we approaching the active (out of range) target?
                // Wait for it (or others) to get into range

                if (Settings.Instance.SpeedTank && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                {
                    if ((DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 15))
                    {
                        target.Orbit(Cache.Instance.OrbitDistance);
                        Logging.Log("MissionController.ClearPocket: Initiating Orbit [" + target.Name + "][" + target.Id + "]");
                        _lastOrbit = DateTime.Now;
                    }
                }

                if (!Settings.Instance.SpeedTank) //we need to make sure that orbitrange is set to the range of the ship if it isnt specified in the character XML!!!!
                {
                    if (Settings.Instance.OptimalRange >= 0)
                    {
                        if (target.Distance > Settings.Instance.OptimalRange + (int)Distance.OptimalRangeCushion && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                        {
                            target.Approach(Settings.Instance.OptimalRange);
                            Logging.Log("MissionController.ClearPocket: Using Optimal Range: Approaching target [" + target.Name + "][" + target.Id + "]");
                        }

                        if (target.Distance <= Settings.Instance.OptimalRange && Cache.Instance.Approaching != null)
                        {
                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                            Cache.Instance.Approaching = null;
                            Logging.Log("MissionController.ClearPocket: Using Optimal Range: Stop ship, target is in orbit range");
                        }
                    }
                    else
                    {
                        if (target.Distance > range && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                        {
                            target.Approach((int)(Cache.Instance.WeaponRange * 0.8d));
                            Logging.Log("MissionController.ClearPocket: Using Weapons Range: Approaching target [" + target.Name + "][" + target.Id + "]");
                        }

                        if (target.Distance <= range && Cache.Instance.Approaching != null)
                        {
                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                            Cache.Instance.Approaching = null;
                            Logging.Log("MissionController.ClearPocket: Using Weapons Range: Stop ship, target is in orbit range");
                        }
                    }
                }
                return;
            }

            // Do we have a timeout?  No, set it to now + 5 seconds
            if (!_clearPocketTimeout.HasValue)
                _clearPocketTimeout = DateTime.Now.AddSeconds(5);

            // Are we in timeout?
            if (DateTime.Now < _clearPocketTimeout.Value)
                return;

            // We have cleared the Pocket, perform the next action \o/
            _currentAction++;

            // Reset timeout
            _clearPocketTimeout = null;
        }

        private void ClearWithinWeaponsRangeOnlyAction(Action action)
        {
             // Get lowest range
            var range = Math.Min(Cache.Instance.WeaponRange, Cache.Instance.DirectEve.ActiveShip.MaxTargetRange);
            var distancetoconsidertargets = range;
            
            var target = Cache.Instance.PriorityTargets.OrderBy(t => t.Distance).Where(t => t.Distance < distancetoconsidertargets && !(Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()) && !Cache.Instance.TargetedBy.Any(w => w.IsWarpScramblingMe || w.IsNeutralizingMe || w.IsWebbingMe))).FirstOrDefault();

            // Or is there a target within distancetoconsidertargets that is targeting us?
            target = target ?? Cache.Instance.TargetedBy.Where(t => t.Distance < distancetoconsidertargets && !t.IsSentry && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();
            // Or is there any target within distancetoconsidertargets?
            target = target ?? Cache.Instance.Entities.Where(t => t.Distance < distancetoconsidertargets && !t.IsSentry && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();
            int targetedby = Cache.Instance.TargetedBy.Where(t => t.Distance < distancetoconsidertargets && !t.IsSentry && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).Count();

            // Reset timeout
            _clearPocketTimeout = null;

            // Lock priority target if within weapons range
            if (target.Distance < range)
            {
                if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                {
                        if (target.IsTarget) //This target is already targeted no need to target it again
                        {
                            return;
                        }
                        else
                        {
                            Logging.Log("MissionController.ClearwithinWeaponsRange: Targeting [" + target.Name + "][" + target.Id + "] - Distance [" + target.Distance + "]");
                            target.LockTarget();
                        }
                }
                return;
            }
            

            // Do we have a timeout?  No, set it to now + 5 seconds
            if (!_clearPocketTimeout.HasValue)
                _clearPocketTimeout = DateTime.Now.AddSeconds(5);

            // Are we in timeout?
            if (DateTime.Now < _clearPocketTimeout.Value)
                return;

            Logging.Log("MissionController: ClearWithinWeaponsRangeOnlyAction is complete: no more targets in weapons range");
            _currentAction++;

            // Reset timeout
            _clearPocketTimeout = null;
        }

        private void MoveToBackgroundAction(Action action)
        {
            if (Cache.Instance.NormalApproch)
                Cache.Instance.NormalApproch = false;

            //int distancetoapp;
            //if (!int.TryParse(action.GetParameterValue("distance"), out distancetoapp))
            //    distancetoapp = (int)Distance.GateActivationRange;

            var target = action.GetParameterValue("target");

            // No parameter? Although we shouldnt really allow it, assume its the acceleration gate :)
            if (string.IsNullOrEmpty(target))
                target = "Acceleration Gate";

            var targets = Cache.Instance.EntitiesByName(target);
            if (targets == null || targets.Count() == 0)
            {
                // Unlike activate, no target just means next action
                _currentAction++;
                return;
            }

            var closest = targets.OrderBy(t => t.Distance).First();
            // Move to the target
            Logging.Log("MissionController.MoveToBackground: Approaching target [" + closest.Name + "][" + closest.Id + "]");
            closest.Approach();
           _currentAction++;
        }

        private void MoveToAction(Action action)
        {
            if (Cache.Instance.NormalApproch)
                Cache.Instance.NormalApproch = false;

            var target = action.GetParameterValue("target");

            // No parameter? Although we shouldnt really allow it, assume its the acceleration gate :)
            if (string.IsNullOrEmpty(target))
                target = "Acceleration Gate";

            int distancetoapp;
            if (!int.TryParse(action.GetParameterValue("distance"), out distancetoapp))
                distancetoapp = (int)Distance.GateActivationRange;

            var targets = Cache.Instance.EntitiesByName(target);
            if (targets == null || targets.Count() == 0)
            {
                // Unlike activate, no target just means next action
                _currentAction++;
                return;
            }

            var closest = targets.OrderBy(t => t.Distance).First();
            if (closest.Distance < distancetoapp)
            {
                // We are close enough to whatever we needed to move to
                _currentAction++;

                if (Cache.Instance.Approaching != null)
                {
                    
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                    Cache.Instance.Approaching = null;
                    Logging.Log("MissionController.MoveTo: Stop ship, we are [" + distancetoapp + "] from [" + closest.Name + "]");
                }
                //if (Settings.Instance.SpeedTank)
                //{
                //    //this should at least keep speed tanked ships from going poof if a mission XML uses moveto
                //    closest.Orbit(Cache.Instance.OrbitDistance);
                //    Logging.Log("MissionController: MoveTo: Initiating orbit after reaching target")
                //}
            }
            else if (closest.Distance < (int)Distance.WarptoDistance)
            {
                    // Move to the target
                    if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id)
                    {
                        Logging.Log("MissionController.Activate: Approaching target [" + closest.Name + "][" + closest.Id + "]");
                        closest.Approach();
                        //_lastApproach = DateTime.Now;
                    }
            }
            else
            {
                //// Move to the target
                //if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id)
                //{
                //    Logging.Log("MissionController.MoveTo: Approaching target [" + closest.Name + "][" + closest.Id + "]");
                //    closest.Approach();
                //}
                // We cant warp if we have drones out
                if (Cache.Instance.ActiveDrones.Count() > 0)
                    return;

                if (DateTime.Now.Subtract(_lastAlign ).TotalMinutes > (int)Time.LastAlignDelay_minutes)
                {
                // Probably never happens
                closest.AlignTo();
                _lastAlign = DateTime.Now;
                }
            }
        }

        private void WaitUntilTargeted(Action action)
        {
            var targetedBy = Cache.Instance.TargetedBy;
            if (targetedBy != null && targetedBy.Count() > 0)
            {
                Logging.Log("MissionController.WaitUntilTargeted: We have been targeted!");

                // We have been locked, go go go ;)
                _waiting = false;
                _currentAction++;
                return;
            }

            // Default timeout is 30 seconds
            int timeout;
            if (!int.TryParse(action.GetParameterValue("timeout"), out timeout))
                timeout = 30; // Probably don't need to do this

            if (_waiting)
            {
                if (DateTime.Now.Subtract(_waitingSince).TotalSeconds < timeout)
                    return;

                Logging.Log("MissionController.WaitUntilTargeted: Nothing targeted us within the timeout!");

                // Nothing has targeted us in the specified timeout
                _waiting = false;
                _currentAction++;
                return;
            }

            // Start waiting
            _waiting = true;
            _waitingSince = DateTime.Now;
        }

        private void AggroOnlyAction(Action action)
        {
            if (Cache.Instance.NormalApproch)
                Cache.Instance.NormalApproch = false;

            bool ignoreAttackers;
            if (!bool.TryParse(action.GetParameterValue("ignoreattackers"), out ignoreAttackers))
                ignoreAttackers = false;

            bool breakOnAttackers;
            if (!bool.TryParse(action.GetParameterValue("breakonattackers"), out breakOnAttackers))
                breakOnAttackers = false;

            bool nottheclosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out nottheclosest))
                nottheclosest = false;

            int numbertoignore;
            if (!int.TryParse(action.GetParameterValue("numbertoignore"), out numbertoignore))
                numbertoignore = 0;

            var targetNames = action.GetParameterValues("target");
            // No parameter? Ignore kill action
            if (targetNames.Count == 0)
            {
                Logging.Log("MissionController.AggroOnly: No targets defined!");

                _currentAction++;
                return;
            }

            var targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name));
            if (targets.Count() == numbertoignore)
            {
                Logging.Log("MissionController.AggroOnly: All targets gone " + targetNames.Aggregate((current, next) => current + "[" + next + "]"));

                // We killed it/them !?!?!? :)
                _currentAction++;
                return;
            }

            if (Cache.Instance.TargetedBy.Any(t => !t.IsSentry && targetNames.Contains(t.Name)))
            {
                // We are being attacked, break the kill order
                if (Cache.Instance.RemovePriorityTargets(targets))
                    Logging.Log("MissionController.AggroOnly: Done with AggroOnly: We have aggro.");

                foreach (var target in Cache.Instance.Targets.Where(e => targets.Any(t => t.Id == e.Id)))
                {
                    Logging.Log("MissionController.AggroOnly: Unlocking [" + target.Name + "][" + target.Id + "] Distance [" + target.Distance + "] due to aggro being obtained");
                    target.UnlockTarget();

                }
                _currentAction++;
                return;
            }

            if (!ignoreAttackers || breakOnAttackers)
            {
                // Apparently we are busy, wait for combat to clear attackers first
                var targetedBy = Cache.Instance.TargetedBy;
                if (targetedBy != null && targetedBy.Count(t => !t.IsSentry && t.Distance < Cache.Instance.WeaponRange) > 0)
                    return;
            }

            var closest = targets.OrderBy(t => t.Distance).First();

            if (nottheclosest)
                closest = targets.OrderByDescending(t => t.Distance).First();

           
            if (!Cache.Instance.PriorityTargets.Any(pt => pt.Id == closest.Id))
            {
                Logging.Log("MissionController.AggroOnly: Adding [" + closest.Name + "][" + closest.Id + "] as a priority target");
                Cache.Instance.AddPriorityTargets(new[] { closest }, Priority.PriorityKillTarget);
            }
        }

        private void KillAction(Action action)
        {
            if (Cache.Instance.NormalApproch)
                Cache.Instance.NormalApproch = false;

            bool ignoreAttackers;
            if (!bool.TryParse(action.GetParameterValue("ignoreattackers"), out ignoreAttackers))
                ignoreAttackers = false;

            bool breakOnAttackers;
            if (!bool.TryParse(action.GetParameterValue("breakonattackers"), out breakOnAttackers))
                breakOnAttackers = false;

            bool nottheclosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out nottheclosest))
                nottheclosest = false;

            int numbertoignore;
            if (!int.TryParse(action.GetParameterValue("numbertoignore"), out numbertoignore))
                numbertoignore = 0;

            var targetNames = action.GetParameterValues("target");
            // No parameter? Ignore kill action
            if (targetNames.Count == 0)
            {
                Logging.Log("MissionController.Kill: No targets defined!");

                _currentAction++;
                return;
            }

            var range = Math.Min(Cache.Instance.WeaponRange, Cache.Instance.DirectEve.ActiveShip.MaxTargetRange);

            var targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name));
            if (targets.Count() == numbertoignore)
            {
                Logging.Log("MissionController.Kill: All targets killed " + targetNames.Aggregate((current, next) => current + "[" + next + "]"));

                // We killed it/them !?!?!? :)
                _currentAction++;
                return;
            }

            if (breakOnAttackers && Cache.Instance.TargetedBy.Any(t => !t.IsSentry && t.Distance < Cache.Instance.WeaponRange))
            {
                // We are being attacked, break the kill order
                if (Cache.Instance.RemovePriorityTargets(targets))
                    Logging.Log("MissionController.Kill: Breaking off kill order, new spawn has arived!");

                foreach (var target in Cache.Instance.Targets.Where(e => targets.Any(t => t.Id == e.Id)))
                {
                    Logging.Log("MissionController.Kill: Unlocking [" + target.Name + "][" + target.Id + "] Distance [" + target.Distance + "] due to kill order being put on hold");
                    target.UnlockTarget();
                }

                return;
            }

            if (!ignoreAttackers || breakOnAttackers)
            {
                // Apparently we are busy, wait for combat to clear attackers first
                var targetedBy = Cache.Instance.TargetedBy;
                if (targetedBy != null && targetedBy.Count(t => !t.IsSentry && t.Distance < Cache.Instance.WeaponRange) > 0)
                    return;
            }

            var closest = targets.OrderBy(t => t.Distance).First();

            if (nottheclosest)
                closest = targets.OrderByDescending(t => t.Distance).First();

            if (closest.Distance < range)
            {
                if (!Cache.Instance.PriorityTargets.Any(pt => pt.Id == closest.Id))
                {
                    Logging.Log("MissionController.Kill: Adding [" + closest.Name + "][" + closest.Id + "] as a priority target");
                    Cache.Instance.AddPriorityTargets(new[] {closest}, Priority.PriorityKillTarget);
                }
                
                if (Cache.Instance.Approaching != null && !Settings.Instance.SpeedTank && (Settings.Instance.OptimalRange <= 0))
                {
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                    Cache.Instance.Approaching = null;
                    Logging.Log("MissionController.Kill: Stop ship, target is in weapons range");
                }
            }

            //if optimalrange is setup and distance to target is less than 80% of optimalrange and we aren't speedtanking
            if (Settings.Instance.OptimalRange > 0 && (closest.Distance < (Settings.Instance.OptimalRange * 0.8d)) && !Settings.Instance.SpeedTank)
            {  
                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                Cache.Instance.Approaching = null;
                Logging.Log("MissionController.Kill: Stop ship, target is in optimalRange");
            }

            //if distance to target is more than weapons range and we havent setup optimalrange OR we are inside optimalrange and optimalrange has been setup
            if ((closest.Distance > range && Settings.Instance.OptimalRange <= 0) || (closest.Distance > Settings.Instance.OptimalRange + (int)Distance.OptimalRangeCushion) && Settings.Instance.OptimalRange > 0)
            {

                //Logging.Log("MissionController: kill: we are out of range)");
                Cache.Instance.TimeSpentInMissionOutOfRange = Cache.Instance.TimeSpentInMissionOutOfRange + ((int)Time.QuestorPulse_milliseconds / 1000); //their has to be a more precise way to do this...
                if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id)
                {
                    //Logging.Log("MissionController: kill: if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id)");
                    if (Settings.Instance.SpeedTank)
                    {
                        if ((DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 15))
                        {
                            closest.Orbit(Cache.Instance.OrbitDistance);
                            Logging.Log("MissionController.Kill: Initiating Orbit [" + closest.Name + "][" + closest.Id + "]");
                            _lastOrbit = DateTime.Now;
                        }

                    }
                    else if (Settings.Instance.OptimalRange > 0)
                    {
                        closest.Approach((int)(Settings.Instance.OptimalRange * 0.8d)); // Move within 80% of optimalrange
                        Logging.Log("MissionController: Kill: initiating Approach of [" + closest.Name + "] distance: [" + closest.Distance + "] approaching to [" + (Settings.Instance.OptimalRange * 0.8d) + "]");
                    }
                    else
                    {
                        closest.Approach((int)(Cache.Instance.WeaponRange * 0.8d)); // Move within 80% of range
                        Logging.Log("MissionController: Kill: initiating Approach of [" + closest.Name + "] distance: [" + closest.Distance + "] approaching to [" + (Settings.Instance.OptimalRange * 0.8d) + "]");
                    }
                }
                else
                {
                    if ((DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 15)) //abuse the lastorbit timestamp here - ugly
                    {
                        //Logging.Log("MissionController: kill: we are already on approach to the target");
                    }
                }
            }
            else
            {
                if ((DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 15)) //abuse the lastorbit timestamp here - ugly
                {
                    Cache.Instance.TimeSpentInMissionInRange = Cache.Instance.TimeSpentInMissionInRange + ((int)Time.QuestorPulse_milliseconds / 1000); //their has to be a more precise way to do this...
                    //Logging.Log("MissionController: kill: target(s) are IN range");
                }
            }
        }

        private void KillOnceAction(Action action)
        {

            Logging.Log("This action (KillOnce) is not yet enabled: proceeding to next action");
            _currentAction++;
            //some impossible speed - to quiet a compile warning until this action gets fixed
            
            if (Cache.Instance.DirectEve.ActiveShip.Entity.Velocity > 9999999) //this concept with a relatively more realistic speed should be used in places to make sure speed tanks are moving
            {
                /* if (Cache.Instance.NormalApproch)
                    Cache.Instance.NormalApproch = false;

                bool ignoreAttackers;
                if (!bool.TryParse(action.GetParameterValue("ignoreattackers"), out ignoreAttackers))
                    ignoreAttackers = false;

                bool breakOnAttackers;
                if (!bool.TryParse(action.GetParameterValue("breakonattackers"), out breakOnAttackers))
                    breakOnAttackers = false;

                bool nottheclosest;
                if (!bool.TryParse(action.GetParameterValue("notclosest"), out nottheclosest))
                    nottheclosest = false;

                int numbertoignore;
                if (!int.TryParse(action.GetParameterValue("numbertoignore"), out numbertoignore))
                    numbertoignore = 0;

                var targetNames = action.GetParameterValues("target");
                // No parameter? Ignore kill action
                if (targetNames.Count == 0)
                {
                    Logging.Log("MissionController.KillOnce: No targets defined!");

                    _currentAction++;
                    return;
            }


                if (Cache.Instance.CurrentCombatTargets.Count == 0 )
                {
                    if (!nottheclosest) //default is the closest target
                    {
                //        Cache.Instance.CurrentCombatTargets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).OrderBy(t => t.Distance).First();
                    }
                    else if (nottheclosest) // if specified then reverse the entity search so that we are targeting the furthest target
                    {                    
                //        Cache.Instance.CurrentCombatTargets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).OrderByDescending(t => t.Distance).First();
                        //Cache.Instance.CurrentCombatTargets = Cache.Instance.Entities.Where(e => e.IsContainer && e.HaveLootRights || e.GroupId == (int) Group.Wreck)).OrderBy(e => e.Distance).ToList();

                    }
                }
                var target = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).OrderBy(t => t.Distance).First();
                //var containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderBy(e => e.Distance); 
                //        target =   Cache.Instance.Entities.Where(e =>  Cache.Instance.CurrentCombatTargets.Contains(e.Id).Orderby(t => target.Distance).First();
                //
                // is it dead?
                //
                //if (target.)
                //{
                //    Logging.Log("MissionController.KillOnce: The target is dead, not valid anymore ");
                //
                //    // We killed it/them !?!?!? :)
                //    _currentAction++;
                //    return;
                //}

                if (!ignoreAttackers || breakOnAttackers)
                {
                    // Apparently we are busy, wait for combat to clear attackers first
                    var targetedBy = Cache.Instance.TargetedBy;
                    if (targetedBy != null && targetedBy.Count(t => !t.IsSentry && t.Distance < Cache.Instance.WeaponRange) > 0)
                        return;
                }


                if (target.Distance < Cache.Instance.WeaponRange)
                {
                    if (!Cache.Instance.PriorityTargets.Any(pt => pt.Id == target.Id))
                    {
                        Logging.Log("MissionController.KillOnce: Adding [" + target.Name + "][" + target.Id + "] as a priority target");
                        Cache.Instance.AddPriorityTargets(new[] { target }, Priority.PriorityKillTarget);
                    }

                    if (Cache.Instance.Approaching != null && !Settings.Instance.SpeedTank)
                    {
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                        Cache.Instance.Approaching = null;
                        Logging.Log("MissionController.KillOnce: Stop ship, target is in weapons range");
                    }
                }
                else
                {
                    // Move within 80% max distance
                    if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id)
                    {
                        Logging.Log("MissionController.KillOnce: Approaching target [" + target.Name + "][" + target.Id + "]");

                        if (Settings.Instance.SpeedTank)
                        {
                            if (DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 15)
                            {
                                target.Orbit(Cache.Instance.OrbitDistance);
                                Logging.Log("MissionController: killonce: initiating orbit");
                                _lastOrbit = DateTime.Now;
                            }
                        }
                        else
                        {
                            target.Approach((int)(Cache.Instance.WeaponRange * 0.8d));
                            Logging.Log("MissionController: killonce: approaching");
                        }
                    }
                } */
            }
        }

        private void AttackClosestByNameAction(Action action)
        {
            bool nottheclosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out nottheclosest))
                nottheclosest = false; 
            
            if (Cache.Instance.NormalApproch)
                Cache.Instance.NormalApproch = false;

            var range = Math.Min(Cache.Instance.WeaponRange, Cache.Instance.DirectEve.ActiveShip.MaxTargetRange);

            var targetNames = action.GetParameterValues("target");
            // No parameter? Ignore kill action
            if (targetNames.Count == 0)
            {
                Logging.Log("MissionController.AttackClosestByName: No targets defined!");

                _currentAction++;
                return;
            }

            //var targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name));
            var target = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).OrderBy(t => t.Distance).First();
            if (nottheclosest)
                target = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).OrderByDescending(t => t.Distance).First();

            if (!target.IsValid)
            {
                Logging.Log("MissionController.AttackClosestByName: All targets killed, not valid anymore ");

                // We killed it/them !?!?!? :)
                _currentAction++;
                return;
            }

            if (target.Distance < range)
            {
                if (!Cache.Instance.PriorityTargets.Any(pt => pt.Id == target.Id))
                {
                    Logging.Log("MissionController.AttackClosestByName: Adding [" + target.Name + "][" + target.Id + "] as a priority target");
                    Cache.Instance.AddPriorityTargets(new[] { target }, Priority.PriorityKillTarget);
                }

                if (Cache.Instance.Approaching != null && !Settings.Instance.SpeedTank && (Settings.Instance.OptimalRange <= 0))
                {
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                    Cache.Instance.Approaching = null;
                    Logging.Log("MissionController.AttackClosestByName: Stop ship, target is in weapons range");
                }
            }

            //if optimalrange is setup and distance to target is less than 80% of optimalrange and we aren't speedtanking
            if (Settings.Instance.OptimalRange > 0 && (target.Distance < (Settings.Instance.OptimalRange * 0.8d)) && !Settings.Instance.SpeedTank)
            {
                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                Cache.Instance.Approaching = null;
                Logging.Log("MissionController.AttackClosestByName: Stop ship, target is in optimalRange");
            }

            //if distance to target is more than weapons range and we havent setup optimalrange OR we are inside optimalrange and optimalrange has been setup
            if ((target.Distance > range && Settings.Instance.OptimalRange <= 0) || (target.Distance > Settings.Instance.OptimalRange + (int)Distance.OptimalRangeCushion) && Settings.Instance.OptimalRange > 0)
            {
                if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id)
                {
                    Logging.Log("MissionController.AttackClosestByName: Approaching target [" + target.Name + "][" + target.Id + "]");

                    if (Settings.Instance.SpeedTank)
                    {
                        if (DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 15)
                        {
                            target.Orbit(Cache.Instance.OrbitDistance); //orbit
                            Logging.Log("MissionController: AttackClosestByName: initiating Orbit of [" + target.Name + "] orbiting at [" + Cache.Instance.OrbitDistance + "]");
                            _lastOrbit = DateTime.Now;
                        }
                    }
                    else if (Settings.Instance.OptimalRange > 0)
                    {
                        target.Approach((int)(Settings.Instance.OptimalRange * 0.8d)); // Move within 80% of optimalrange
                        Logging.Log("MissionController: AttackClosestByName: initiating Approach of [" + target.Name + "] distance: [" + target.Distance + "] approaching to [" + (Settings.Instance.OptimalRange * 0.8d) + "]");
                    }
                    else
                    {
                        target.Approach((int)(Cache.Instance.WeaponRange * 0.8d)); // Move within 80% of range
                        Logging.Log("MissionController: AttackClosestByName: initiating Approach of [" + target.Name + "] distance: [" + target.Distance + "] approaching to [" + (Settings.Instance.OptimalRange * 0.8d) + "]");
                    }
                }
            }
        }

        private void AttackClosestAction(Action action)
        {
            if (Cache.Instance.NormalApproch)
                Cache.Instance.NormalApproch = false;

            bool nottheclosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out nottheclosest))
                nottheclosest = false;

            var range = Math.Min(Cache.Instance.WeaponRange, Cache.Instance.DirectEve.ActiveShip.MaxTargetRange);

            var targetNames = action.GetParameterValues("target");
            // No parameter? Ignore kill action
            if (targetNames.Count == 0)
            {
                Logging.Log("MissionController.AttackClosest: No targets defined!");

                _currentAction++;
                return;
            }

            //var targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name));
            var target = Cache.Instance.Entities.OrderBy(t => t.Distance).First();
            if (nottheclosest)
                target = Cache.Instance.Entities.OrderByDescending(t => t.Distance).First();
            if (!target.IsValid)
            {
                Logging.Log("MissionController.AttackClosest: All targets killed, not valid anymore ");

                // We killed it/them !?!?!? :)
                _currentAction++;
                return;
            }

            if (target.Distance < range)
            {
                if (!Cache.Instance.PriorityTargets.Any(pt => pt.Id == target.Id))
                {
                    Logging.Log("MissionController.AttackClosest: Adding [" + target.Name + "][" + target.Id + "] as a priority target");
                    Cache.Instance.AddPriorityTargets(new[] { target }, Priority.PriorityKillTarget);
                }

                if (Cache.Instance.Approaching != null && !Settings.Instance.SpeedTank && (Settings.Instance.OptimalRange <= 0))
                {
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                    Cache.Instance.Approaching = null;
                    Logging.Log("MissionController.AttackClosest: Stop ship, target is in weapons range");
                }
            }

            //if optimalrange is setup and distance to target is less than 80% of optimalrange and we aren't speedtanking
            if (Settings.Instance.OptimalRange > 0 && (target.Distance < (Settings.Instance.OptimalRange * 0.8d)) && !Settings.Instance.SpeedTank)
            {
                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                Cache.Instance.Approaching = null;
                Logging.Log("MissionController.AttackClosest: Stop ship, target is in optimalRange");
            }

            //if distance to target is more than weapons range and we havent setup optimalrange OR we are inside optimalrange and optimalrange has been setup
            if ((target.Distance > range && Settings.Instance.OptimalRange <= 0) || (target.Distance > Settings.Instance.OptimalRange + (int)Distance.OptimalRangeCushion) && Settings.Instance.OptimalRange > 0)
            {
                if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id)
                {
                    Logging.Log("MissionController.AttackClosest: Approaching target [" + target.Name + "][" + target.Id + "]");

                    if (Settings.Instance.SpeedTank)
                    {
                        if (DateTime.Now.Subtract(_lastOrbit).TotalSeconds > 15)
                        {
                            target.Orbit(Cache.Instance.OrbitDistance); //orbit
                            Logging.Log("MissionController: AttackClosest: initiating Orbit of [" + target.Name + "] orbiting at [" + Cache.Instance.OrbitDistance + "]");
                            _lastOrbit = DateTime.Now;
                            
                        }
                    }
                    else if (Settings.Instance.OptimalRange > 0)
                    {
                        target.Approach((int)(Settings.Instance.OptimalRange * 0.8d)); // Move within 80% of optimalrange
                        Logging.Log("MissionController: AttackClosest: initiating Approach of [" + target.Name + "] distance: [" + target.Distance + "] approaching to [" + (Settings.Instance.OptimalRange * 0.8d) + "]");
                    }
                    else
                    {
                        target.Approach((int)(Cache.Instance.WeaponRange * 0.8d)); // Move within 80% of range
                        Logging.Log("MissionController: AttackClosest: initiating Approach of [" + target.Name + "] distance: [" + target.Distance + "] approaching to [" + (Settings.Instance.OptimalRange * 0.8d) + "]");
                    }
                }
            }
        }

        private void LootItemAction(Action action)
        {
            var items = action.GetParameterValues("item");
            var targetNames = action.GetParameterValues("target");
                // if we arent generally looting we need to re-enable the opening of wrecks to
                // find this LootItems we are looking for
                Cache.Instance.OpenWrecks = true;

            int quantity;
            if (!int.TryParse(action.GetParameterValue("quantity"), out quantity))
                quantity = 1;

            var done = items.Count == 0;
            if (!done)
            {
                var cargo = Cache.Instance.DirectEve.GetShipsCargo();
                // We assume that the ship's cargo will be opened somewhere else
                if (cargo.IsReady)
                    done |= cargo.Items.Any(i => (items.Contains(i.TypeName) && (i.Quantity >= quantity)));
            }
            if (done)
            {
                Logging.Log("MissionController.LootItem: We are done looting");
                    // now that we've completed this action revert OpenWrecks to false
                    Cache.Instance.OpenWrecks = false;
                _currentAction++;
                return;
            }

            var containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderBy(e => e.Distance);
            //var containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderBy(e => e.Id);
            //var containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderByDescending(e => e.Id);
            if (containers.Count() == 0)
            {
                Logging.Log("MissionController.LootItem: We are done looting");

                _currentAction++;
                return;
            }

            var closest = containers.FirstOrDefault(c => targetNames.Contains(c.Name)) ?? containers.First();
            if (closest.Distance > (int)Distance.SafeScoopRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id))
            {
                Logging.Log("MissionController.LootItem: Approaching target [" + closest.Name + "][" + closest.Id + "]");
                closest.Approach();
            }
        }

        private void LootAction(Action action)
        {
            var items = action.GetParameterValues("item");
            var targetNames = action.GetParameterValues("target");
                // if we arent generally looting we need to re-enable the opening of wrecks to
                // find this LootItems we are looking for
                Cache.Instance.OpenWrecks = true;
            if (!Settings.Instance.LootEverything)
            {
                var done = items.Count == 0;
                if (!done)
                {
                    var cargo = Cache.Instance.DirectEve.GetShipsCargo();
                    // We assume that the ship's cargo will be opened somewhere else
                    if (cargo.IsReady)
                        done |= cargo.Items.Any(i => items.Contains(i.TypeName));
                }
                if (done)
                {
                    Logging.Log("MissionController.Loot: We are done looting");
                        // now that we are done with this action revert OpenWrecks to false
                        Cache.Instance.OpenWrecks = false;

                    _currentAction++;
                    return;
                }
            }
            // unlock targets count
            Cache.Instance.MissionLoot = true;

            var containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderBy(e => e.Distance);
            if (containers.Count() == 0)
            {
                // lock targets count
                Cache.Instance.MissionLoot = false;
                Logging.Log("MissionController.Loot: We are done looting");
                // now that we are done with this action revert OpenWrecks to false
                Cache.Instance.OpenWrecks = false;

                _currentAction++;
                return;
            }

            var closest = containers.FirstOrDefault(c => targetNames.Contains(c.Name)) ?? containers.First();
            if (closest.Distance > (int)Distance.SafeScoopRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id))
            {
                Logging.Log("MissionController.Loot: Approaching target [" + closest.Name + "][" + closest.Id + "]");
                closest.Approach();
            }
        }

        private void IgnoreAction(Action action)
        {
            bool clear;
            if (!bool.TryParse(action.GetParameterValue("clear"), out clear))
                clear = false; 
            
            var add = action.GetParameterValues("add");
            var remove = action.GetParameterValues("remove");

            if (clear)
                Cache.Instance.IgnoreTargets.Clear();
            else
            {
                add.ForEach(a => Cache.Instance.IgnoreTargets.Add(a.Trim()));
                remove.ForEach(a => Cache.Instance.IgnoreTargets.Remove(a.Trim()));
            }
            Logging.Log("MissionController.Ignore: Updated ignore list");
            if (Cache.Instance.IgnoreTargets.Any())
                Logging.Log("MissionController.Ignore: Currently ignoring: " + Cache.Instance.IgnoreTargets.Aggregate((current, next) => current + "[" + next + "]"));
            else
                Logging.Log("MissionController.Ignore: Your ignore list is empty");
            _currentAction++;
        }

        private void PerformAction(Action action)
        {
            switch (action.State)
            {
                case ActionState.Activate:
                    ActivateAction(action);
                    break;

                case ActionState.ClearPocket:
                    ClearPocketAction(action);
                    break;

                case ActionState.SalvageBookmark:
                    BookmarkPocketForSalvaging();

                    _currentAction++;
                    break;

                case ActionState.Done:
                    // Tell the drones module to retract drones
                    Cache.Instance.IsMissionPocketDone = true;

                    // We do not switch to "done" status if we still have drones out
                    if (Cache.Instance.ActiveDrones.Count() > 0)
                        return;

                    // Add bookmark (before we're done)
                    if (Settings.Instance.CreateSalvageBookmarks)
                        BookmarkPocketForSalvaging();

                    // Reload weapons
                    if (DateTime.Now.Subtract(_lastReload).TotalSeconds > 20)
                    {
                        Logging.Log("MissionController: ReloadAll: Reload becasue ActionState is Done - Reloading Weapons.");
                        ReloadAll();
                        _lastReload = DateTime.Now;
                    }

                    State = MissionControllerState.Done;
                    break;

                case ActionState.Kill:
                    KillAction(action);
                    break;

                case ActionState.KillOnce:
                    KillOnceAction(action);
                    break;

                case ActionState.AttackClosestByName:
                    AttackClosestByNameAction(action);
                    break;

                case ActionState.AttackClosest:
                    AttackClosestAction(action);
                    break;

                case ActionState.MoveTo:
                    MoveToAction(action);
                    break;

                case ActionState.MoveToBackground:
                    MoveToBackgroundAction(action);
                    break;
                
                case ActionState.ClearWithinWeaponsRangeOnly:
                    ClearWithinWeaponsRangeOnlyAction(action);
                    break;

                case ActionState.Loot:
                    LootAction(action);
                    break;

                case ActionState.LootItem:
                    LootItemAction(action);
                    break;

                case ActionState.Ignore:
                    IgnoreAction(action);
                    break;

                case ActionState.WaitUntilTargeted:
                    WaitUntilTargeted(action);
                    break;
            }
        }

        public void ProcessState()
        {
            // What? No ship entity?
            if (Cache.Instance.DirectEve.ActiveShip.Entity == null)
                return;

            switch (State)
            {
                case MissionControllerState.Idle:
                    break;
                case MissionControllerState.Done:
                    LogStatistics();

                    if (!Cache.Instance.NormalApproch)
                        Cache.Instance.NormalApproch = true;

                    Cache.Instance.IgnoreTargets.Clear();
                    break;
                case MissionControllerState.Error:
                    break;

                case MissionControllerState.Start:
                    _pocket = 0;
                    // Update statistic values
                    Wealth = Cache.Instance.DirectEve.Me.Wealth;
                    StartedPocket = DateTime.Now;

                    // Reload the items needed for this mission from the XML file
                    Cache.Instance.RefreshMissionItems(AgentId);

                    // Update x/y/z so that NextPocket wont think we are there yet because its checking (very) old x/y/z cords
                    _lastX = Cache.Instance.DirectEve.ActiveShip.Entity.X;
                    _lastY = Cache.Instance.DirectEve.ActiveShip.Entity.Y;
                    _lastZ = Cache.Instance.DirectEve.ActiveShip.Entity.Z;

                    State = MissionControllerState.LoadPocket;
                    break;

                case MissionControllerState.LoadPocket:
                    _pocketActions.Clear();
                    _pocketActions.AddRange(Cache.Instance.LoadMissionActions(AgentId, _pocket, true));

                    //
                    // LogStatistics();
                    //
                    if (_pocketActions.Count == 0)
                    {
                        // No Pocket action, load default actions
                        Logging.Log("MissionController: No mission actions specified, loading default actions");

                        // Wait for 30 seconds to be targeted
                        _pocketActions.Add(new Action {State = ActionState.WaitUntilTargeted});
                        _pocketActions[0].AddParameter("timeout", "15");

                        // Clear the Pocket
                        _pocketActions.Add(new Action {State = ActionState.ClearPocket});

                        // Is there a gate?
                        var gates = Cache.Instance.EntitiesByName("Acceleration Gate");
                        if (gates != null && gates.Count() > 0)
                        {
                            // Activate it (Activate action also moves to the gate)
                            _pocketActions.Add(new Action {State = ActionState.Activate});
                            _pocketActions[_pocketActions.Count - 1].AddParameter("target", "Acceleration Gate");
                        }
                        else // No, were done
                            _pocketActions.Add(new Action {State = ActionState.Done});

                        // TODO: Check mission HTML to see if we need to pickup any items
                        // Not a priority, apparently retrieving HTML causes a lot of crashes
                    }

                    Logging.Log("MissionController: Pocket loaded, executing the following actions");
                    foreach (var a in _pocketActions)
                        Logging.Log("MissionController: Action." + a);

                    if (Cache.Instance.OrbitDistance != Settings.Instance.OrbitDistance)
                    {
                        if (Cache.Instance.OrbitDistance == 0)
                        {
                            Cache.Instance.OrbitDistance = Settings.Instance.OrbitDistance;
                            Logging.Log("MissionController: Using default orbit distance: " + Cache.Instance.OrbitDistance + " (as the custom one was 0)");
                        }
                        else
                            Logging.Log("MissionController: Using custom orbit distance: " + Cache.Instance.OrbitDistance);
                    }

                    // Reset pocket information
                    _currentAction = 0;
                    Cache.Instance.IsMissionPocketDone = false;
                    Cache.Instance.IgnoreTargets.Clear();

                    State = MissionControllerState.ExecutePocketActions;
                    break;

                case MissionControllerState.ExecutePocketActions:
                    if (_currentAction >= _pocketActions.Count)
                    {
                        // No more actions, but we're not done?!?!?!
                        Logging.Log("MissionController: We're out of actions but did not process a 'Done' or 'Activate' action");

                        State = MissionControllerState.Error;
                        break;
                    }

                    var action = _pocketActions[_currentAction];
                    var currentAction = _currentAction;
                    PerformAction(action);

                    if (currentAction != _currentAction)
                    {
                        Logging.Log("MissionController: Finished Action." + action);

                        if (_currentAction < _pocketActions.Count)
                        {
                            action = _pocketActions[_currentAction];
                            Logging.Log("MissionController: Starting Action." + action);
                        }
                    }

                    if (Settings.Instance.DebugStates)
                        Logging.Log("Action.State = " + action);
                    break;

                case MissionControllerState.NextPocket:
                    var distance = Cache.Instance.DistanceFromMe(_lastX, _lastY, _lastZ);
                    if (distance > (int)Distance.NextPocketDistance)
                    {
                        Logging.Log("MissionController: We've moved to the next Pocket [" + distance + "]");

                        // If we moved more then 100km, assume next Pocket
                        _pocket++;
                        State = MissionControllerState.LoadPocket;
                        LogStatistics();
                        
                    }
                    else if (DateTime.Now.Subtract(_lastActivateAction).TotalMinutes > 2)
                    {
                        Logging.Log("MissionController: We've timed out, retry last action");

                        // We have reached a timeout, revert to ExecutePocketActions (e.g. most likely Activate)
                        State = MissionControllerState.ExecutePocketActions;
                    }
                    break;
            }

            var newX = Cache.Instance.DirectEve.ActiveShip.Entity.X;
            var newY = Cache.Instance.DirectEve.ActiveShip.Entity.Y;
            var newZ = Cache.Instance.DirectEve.ActiveShip.Entity.Z;

            // For some reason x/y/z returned 0 sometimes
            if (newX != 0 && newY != 0 && newZ != 0)
            {
                // Save X/Y/Z so that NextPocket can check if we actually went to the next Pocket :)
                _lastX = newX;
                _lastY = newY;
                _lastZ = newZ;
            }
        }
    }
}