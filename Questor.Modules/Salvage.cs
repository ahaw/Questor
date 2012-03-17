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
    using System.Linq;
    using DirectEve;
    using System.IO;

    public class Salvage
    {
        public static HashSet<int> Salvagers = new HashSet<int> { 25861, 26983, 30836 };
        public static HashSet<int> TractorBeams = new HashSet<int> { 24348, 24620, 24622, 24644, 4250 };

        private DateTime _lastJettison = DateTime.MinValue;
        private DateTime _nextSalvageAction = DateTime.MinValue;
        private DateTime _nextApproachAction = DateTime.MinValue;
        private DateTime _nextTargetAction = DateTime.MinValue;

        /// <summary>
        ///   Keep a list of times that we have tried to open a container (do not try to open the same container twice within 10 seconds)
        /// </summary>
        private Dictionary<long, DateTime> _openedContainers;

        public Salvage()
        {
            _openedContainers = new Dictionary<long, DateTime>();
        }

        public int MaximumWreckTargets { get; set; }
        public bool LootEverything { get; set; }
        public int ReserveCargoCapacity { get; set; }
        public List<Ammo> Ammo { get; set; }

        public SalvageState State { get; set; }

        /// <summary>
        ///   Activates tractorbeam on targeted wrecks
        /// </summary>
        private void ActivateTractorBeams()
        {
            if (_nextSalvageAction > DateTime.Now) return;

            var tractorBeams = Cache.Instance.Modules.Where(m => TractorBeams.Contains(m.TypeId)).ToList();
            if (tractorBeams.Count == 0)
                return;

            var tractorBeamRange = tractorBeams.Min(t => t.OptimalRange);
            var wrecks = Cache.Instance.Targets.Where(t => (t.GroupId == (int)Group.Wreck || t.GroupId == (int)Group.CargoContainer) && t.Distance < tractorBeamRange).ToList();
            
            Logging.Log(Cache.Instance.DirectEve.ActiveShip.Entity.Mode.ToString());
            if (wrecks.FirstOrDefault() == null)
            {
                var wrecksFar = Cache.Instance.Entities.Where(t => (t.GroupId == (int)Group.Wreck || t.GroupId == (int)Group.CargoContainer) && t.Distance > tractorBeamRange).ToList();
                if (wrecksFar.Count > 0)
                    if (Cache.Instance.DirectEve.ActiveShip.Entity.Mode != 1)
                    {
                        if (_nextApproachAction < DateTime.Now)
                        {
                            _nextApproachAction = DateTime.Now.AddSeconds((int)Time.ApproachDelay_seconds);
                            wrecksFar.FirstOrDefault().Approach();
                        }
                    }
                State = SalvageState.TargetWrecks;
                return;
            };

            for (var i = tractorBeams.Count - 1; i >= 0; i--)
            {
                var tractorBeam = tractorBeams[i];
                if (!tractorBeam.IsActive && !tractorBeam.IsDeactivating)
                    continue;

                var wreck = wrecks.FirstOrDefault(w => w.Id == tractorBeam.TargetId);
                // If the wreck no longer exists, or its within loot range then disable the tractor beam
                if (tractorBeam.IsActive && (wreck == null || wreck.Distance <= (int)Distance.SafeScoopRange))
                {
                    tractorBeam.Deactivate();
                    _nextSalvageAction = DateTime.Now.AddMilliseconds((int)Time.SalvageDelayBetweenActions_miliseconds);
                    return;
                    //More human behaviour
                    //System.Threading.Thread.Sleep(333);
                }
                // Remove the tractor beam as a possible beam to activate
                tractorBeams.RemoveAt(i);
                wrecks.RemoveAll(w => w.Id == tractorBeam.TargetId);
            }

            foreach (var wreck in wrecks)
            {
                // This velocity check solves some bugs where velocity showed up as 150000000m/s
                if (wreck.Velocity != 0 && wreck.Velocity < (int)Distance.SafeScoopRange)
                    continue;

                // Is this wreck within range?
                if (wreck.Distance < (int)Distance.SafeScoopRange)
                    continue;

                if (tractorBeams.Count == 0)
                    return;

                var tractorBeam = tractorBeams[0];
                tractorBeams.RemoveAt(0);
                tractorBeam.Activate(wreck.Id);

                Logging.Log("Salvage: Activating tractorbeam [" + tractorBeam.ItemId + "] on [" + wreck.Name + "][" + wreck.Id + "]");
                //More human behaviour
                //System.Threading.Thread.Sleep(333);
                _nextSalvageAction = DateTime.Now.AddMilliseconds((int)Time.SalvageDelayBetweenActions_miliseconds);
                return;

            }
        }

        /// <summary>
        ///   Activate salvagers on targeted wreck
        /// </summary>
        private void ActivateSalvagers()
        {
            if (_nextSalvageAction > DateTime.Now) return;
            var salvagers = Cache.Instance.Modules.Where(m => Salvagers.Contains(m.TypeId)).ToList();
            if (salvagers.Count == 0)
                return;

            var salvagerRange = salvagers.Min(s => s.OptimalRange);
            var wrecks = Cache.Instance.Targets.Where(t => t.GroupId == (int)Group.Wreck && t.Distance < salvagerRange && !Settings.Instance.WreckBlackList.Any(a => a == t.TypeId)).ToList();
            if (Cache.Instance.SalvageAll)
                wrecks = Cache.Instance.Targets.Where(t => t.GroupId == (int)Group.Wreck && t.Distance < salvagerRange).ToList();

            if (wrecks.Count == 0)
                return;

            foreach (var salvager in salvagers)
            {
                if (salvager.IsActive || salvager.IsDeactivating)
                    continue;

                // Spread the salvagers around
                var wreck = wrecks.OrderBy(w => salvagers.Count(s => s.LastTargetId == w.Id)).First();
                if (wreck == null)
                    return;

                Logging.Log("Salvage: Activating salvager [" + salvager.ItemId + "] on [" + wreck.Name + "][" + wreck.Id + "]");
                salvager.Activate(wreck.Id);
                _nextSalvageAction = DateTime.Now.AddMilliseconds((int)Time.SalvageDelayBetweenActions_miliseconds);
                return;
                //More human behaviour
                //System.Threading.Thread.Sleep(333);
            }
        }

        /// <summary>
        ///   Target wrecks within range
        /// </summary>
        private void TargetWrecks()
        {
            if (_nextTargetAction > DateTime.Now) return;
            // We are jammed, we do not need to log (Combat does this already)
            if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets == 0)
                return;

            var targets = new List<EntityCache>();
            targets.AddRange(Cache.Instance.Targets);
            targets.AddRange(Cache.Instance.Targeting);

            var hasSalvagers = Cache.Instance.Modules.Any(m => Salvagers.Contains(m.TypeId));
            var wreckTargets = targets.Where(t => (t.GroupId == (int)Group.Wreck || t.GroupId == (int)Group.CargoContainer) && t.CategoryId == (int)CategoryID.Celestial).ToList();

            // Check for cargo containers
            foreach (var wreck in wreckTargets)
            {
                if (Cache.Instance.IgnoreTargets.Contains(wreck.Name))
                {
                    Logging.Log("Salvage: Cargo Container [" + wreck.Name + "][" + wreck.Id + "] on the ignore list, ignoring.");
                    wreck.UnlockTarget();
                    _nextTargetAction = DateTime.Now.AddMilliseconds((int)Time.TargetDelay_miliseconds);
                    continue;
                }

                if(!Cache.Instance.SalvageAll)
                {
                    if(Settings.Instance.WreckBlackList.Any(a => a == wreck.TypeId) && (wreck.Distance < (int)Distance.SafeScoopRange || wreck.IsWreckEmpty))
                    {
                        wreck.UnlockTarget();
                        _nextTargetAction = DateTime.Now.AddMilliseconds((int)Time.TargetDelay_miliseconds);
                        continue;
                    }
                }

                if (hasSalvagers && wreck.GroupId != (int) Group.CargoContainer)
                    continue;

                // Unlock if within loot range
                if (wreck.Distance < (int)Distance.SafeScoopRange)
                {
                    Logging.Log("Salvage: Cargo Container [" + wreck.Name + "][" + wreck.Id + "] within loot range, unlocking container.");
                    wreck.UnlockTarget();
                    _nextTargetAction = DateTime.Now.AddMilliseconds((int)Time.TargetDelay_miliseconds);
                    return;
                }
            }

            if (Cache.Instance.MissionLoot)
            {
                if(wreckTargets.Count >= Math.Min(Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets, Cache.Instance.DirectEve.Me.MaxLockedTargets))
                    return;
            }
            else if (wreckTargets.Count >= MaximumWreckTargets)
                    return;

            var tractorBeams = Cache.Instance.Modules.Where(m => TractorBeams.Contains(m.TypeId)).ToList();
            var tractorBeamRange = 0d;
            if (tractorBeams.Count > 0)
                tractorBeamRange = tractorBeams.Min(t => t.OptimalRange);

            var wrecks = Cache.Instance.UnlootedContainers;
            foreach (var wreck in wrecks.Where(w => !Cache.Instance.IgnoreTargets.Contains(w.Name.Trim())))
            {
                // Its already a target, ignore it
                if (wreck.IsTarget || wreck.IsTargeting)
                    continue;

                if (wreck.Distance > tractorBeamRange)
                    continue;

                if (!wreck.HaveLootRights)
                    continue;

                // No need to tractor a non-wreck within loot range
                if (wreck.GroupId != (int) Group.Wreck && wreck.Distance < (int)Distance.SafeScoopRange)
                    continue;

                if(!Cache.Instance.SalvageAll)
                {
                    if (Settings.Instance.WreckBlackList.Any(a => a == wreck.TypeId) && (wreck.IsWreckEmpty || wreck.Distance < (int)Distance.SafeScoopRange))
                        continue;
                }

                if (wreck.GroupId != (int) Group.Wreck && wreck.GroupId != (int) Group.CargoContainer)
                    continue;

                if (!hasSalvagers)
                {
                    // Ignore already looted wreck
                    if (Cache.Instance.LootedContainers.Contains(wreck.Id))
                        continue;

                    // Ignore empty wrecks
                    if (wreck.GroupId == (int) Group.Wreck && wreck.IsWreckEmpty)
                        continue;
                }

                Logging.Log("Salvage: Locking [" + wreck.Name + "][" + wreck.Id + "] - Distance [" + wreck.Distance + "]");

                wreck.LockTarget();
                wreckTargets.Add(wreck);
                _nextTargetAction = DateTime.Now.AddMilliseconds((int)Time.TargetDelay_miliseconds);
                if(Cache.Instance.MissionLoot)
                {
                    if(wreckTargets.Count >= Math.Min(Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets, Cache.Instance.DirectEve.Me.MaxLockedTargets))
                        return;
                }
                else
                if (wreckTargets.Count >= MaximumWreckTargets)
                        return;
                return;
            }
        }

        /// <summary>
        ///   Loot any wrecks & cargo containers close by
        /// </summary>
        private void LootWrecks()
        {
            var cargo = Cache.Instance.DirectEve.GetShipsCargo();
            if (cargo.Window == null)
            {
                // No, command it to open
                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenCargoHoldOfActiveShip);
                return;
            }

            // Ship's cargo is not ready yet
            if (!cargo.IsReady)
                return;

            var shipsCargo = cargo.Items.Select(i => new ItemCache(i)).ToList();
            var freeCargoCapacity = cargo.Capacity - cargo.UsedCapacity;
            var lootWindows = Cache.Instance.DirectEve.Windows.OfType<DirectContainerWindow>().Where(w => w.Type == "form.LootCargoView");
            foreach (var window in lootWindows)
            {
                // The window is not ready, then continue
                if (!window.IsReady)
                    continue;

                // Get the container
                var containerEntity = Cache.Instance.EntityById(window.ItemId);

                // Does it no longer exist or is it out of transfer range or its looted
                if (containerEntity == null || containerEntity.Distance > (int)Distance.SafeScoopRange || Cache.Instance.LootedContainers.Contains(containerEntity.Id))
                {
                    Logging.Log("Salvage: Closing loot window [" + window.ItemId + "]");
                    window.Close();
                    continue;
                }

                // Get the container that is associated with the cargo container
                var container = Cache.Instance.DirectEve.GetContainer(window.ItemId);

                // List its items
                var items = container.Items.Select(i => new ItemCache(i));

                // Build a list of items to loot
                var lootItems = new List<ItemCache>();

                if (Settings.Instance.WreckLootStatistics)
                {
                    // Log all items found in the wreck
                    File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "TIME: " + string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTime.Now) + "\n");
                    File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "NAME: " + containerEntity.Name + "\n");
                    File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "ITEMS:" + "\n");
                    foreach (var item in items.OrderBy(i => i.TypeId))
                    {
                        File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "TypeID: " + item.TypeId.ToString() + "\n");
                        File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "Name: " + item.Name + "\n");
                        File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "Quantity: " + item.Quantity.ToString() + "\n");
                        File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "=\n");
                    }
                    File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, ";" + "\n");
                }
                //if (freeCargoCapacity < 1000) //this should allow BSs to dump scrapmetal but haulers and noctus' to hold onto it
                //{
                //	// Dump scrap metal if we have any
                //	if (containerEntity.Name == "Cargo Container" && shipsCargo.Any(i => i.IsScrapMetal))
                //	{
                //		foreach (var item in shipsCargo.Where(i => i.IsScrapMetal))
                //		{
                //			container.Add(item.DirectItem);
                //			freeCargoCapacity += item.TotalVolume;
                //		}
                //
                //		shipsCargo.RemoveAll(i => i.IsScrapMetal);
                //	}
                //}
                // Walk through the list of items ordered by highest value item first
                foreach (var item in items.OrderByDescending(i => i.IskPerM3))
                {
                    if (freeCargoCapacity < 1000) //this should allow BSs to not pickup large low value items but haulers and noctus' to scoop everything
                    {
                        // We never want to pick up a cap booster
                        if (item.GroupID == (int)Group.CapacitorGroupCharge)
                            continue;

                        // We never want to pick up metal scraps
                        //if (item.IsScrapMetal)
                        //	continue;
                    }
                    // We pick up loot depending on isk per m3
                    var isMissionItem = Cache.Instance.MissionItems.Contains((item.Name ?? string.Empty).ToLower());

                    // Never pick up contraband (unless its the mission item)
                    if (!isMissionItem && item.IsContraband)
                        continue;

                    // Do we want to loot other items?
                    if (!isMissionItem && !LootEverything)
                        continue;

                    // Do not pick up items that cannot enter in a freighter container (unless its the mission item)
                    // Note: some mission items that are alive have been allowed to be 
                    //       scooped because unloadlootstate.MoveCommonMissionCompletionitems 
                    //       will move them into the hangar floor not the loot location
                    if (!isMissionItem && item.IsAliveandWontFitInContainers)
                        continue;

                    // We are at our max, either make room or skip the item
                    if ((freeCargoCapacity - item.TotalVolume) <= (isMissionItem ? 0 : ReserveCargoCapacity))
                    {
                        // We can't drop items in this container anyway, well get it after its salvaged
                        if (!isMissionItem && containerEntity.GroupId != (int) Group.CargoContainer)
                            continue;

                        // Make a list of items which are worth less
                        List<ItemCache> worthLess;
                        if (isMissionItem)
                            worthLess = shipsCargo;
                        else if (item.IskPerM3.HasValue)
                            worthLess = shipsCargo.Where(sc => sc.IskPerM3.HasValue && sc.IskPerM3 < item.IskPerM3).ToList();
                        else
                            worthLess = shipsCargo.Where(sc => sc.IskPerM3.HasValue).ToList();

                        // Remove mission item from this list
                        worthLess.RemoveAll(wl => Cache.Instance.MissionItems.Contains((wl.Name ?? string.Empty).ToLower()));
                        worthLess.RemoveAll(wl => (wl.Name ?? string.Empty).ToLower() == Cache.Instance.BringMissionItem);

                        // Consider dropping ammo if it concerns the mission item!
                        if (!isMissionItem)
                            worthLess.RemoveAll(wl => Ammo.Any(a => a.TypeId == wl.TypeId));

                        // Nothing is worth less then the current item
                        if (worthLess.Count() == 0)
                            continue;

                        // Not enough space even if we dumped the crap
                        if ((freeCargoCapacity + worthLess.Sum(wl => wl.TotalVolume)) < item.TotalVolume)
                        {
                            if (isMissionItem)
                                Logging.Log("Salvage: Not enough space for mission item! Need [" + item.TotalVolume + "] maximum available [" + (freeCargoCapacity + worthLess.Sum(wl => wl.TotalVolume)) + "]");

                            continue;
                        }

                        // Start clearing out items that are worth less
                        var moveTheseItems = new List<DirectItem>();
                        foreach (var wl in worthLess.OrderBy(wl => wl.IskPerM3.HasValue ? wl.IskPerM3.Value : double.MaxValue).ThenByDescending(wl => wl.TotalVolume))
                        {
                            // Mark this item as moved
                            moveTheseItems.Add(wl.DirectItem);

                            // Substract (now) free volume
                            freeCargoCapacity += wl.TotalVolume;

                            // We freed up enough space?
                            if ((freeCargoCapacity - item.TotalVolume) >= ReserveCargoCapacity)
                                break;
                        }

                        if (moveTheseItems.Count > 0)
                        {
                            // If this is not a cargo container, then jettison loot
                            if (containerEntity.GroupId != (int) Group.CargoContainer || isMissionItem)
                            {
                                if (DateTime.Now.Subtract(_lastJettison).TotalSeconds < (int)Time.DelayBetweenJetcans_seconds)
                                    return;

                                Logging.Log("Salvage: Jettisoning [" + moveTheseItems.Count + "] items to make room for the more valuable loot");

                                // Note: This could (in theory) fuck up with the bot jettison an item and 
                                // then picking it up again :/ (granted it should never happen unless 
                                // mission item volume > reserved volume
                                cargo.Jettison(moveTheseItems.Select(i => i.ItemId));
                                _lastJettison = DateTime.Now;
                                return;
                            }

                            // Move items to the cargo container
                            container.Add(moveTheseItems);

                            // Remove it from the ships cargo list
                            shipsCargo.RemoveAll(i => moveTheseItems.Any(wl => wl.ItemId == i.Id));
                            Logging.Log("Salvage: Moving [" + moveTheseItems.Count + "] items into the cargo container to make room for the more valuable loot");
                        }
                    }

                    // Update free space
                    freeCargoCapacity -= item.TotalVolume;
                    lootItems.Add(item);
                }


                // Mark container as looted
                Cache.Instance.LootedContainers.Add(containerEntity.Id);

                // Loot actual items
                if (lootItems.Count != 0)
                {
                    Logging.Log("Salvage: Looting container [" + containerEntity.Name + "][" + containerEntity.Id + "], [" + lootItems.Count + "] valuable items");
                    cargo.Add(lootItems.Select(i => i.DirectItem));
                }
                else
                    Logging.Log("Salvage: Container [" + containerEntity.Name + "][" + containerEntity.Id + "] contained no valuable items");
            }

            // Open a container in range
            foreach (var containerEntity in Cache.Instance.Containers.Where(e => e.Distance <= (int)Distance.SafeScoopRange))
            {
                // Emptry wreck, ignore
                if (containerEntity.GroupId == (int) Group.Wreck && containerEntity.IsWreckEmpty)
                    continue;

                // We looted this container
                if (Cache.Instance.LootedContainers.Contains(containerEntity.Id))
                    continue;

                // We already opened the loot window
                var window = lootWindows.FirstOrDefault(w => w.ItemId == containerEntity.Id);
                if (window != null)
                    continue;

                // Ignore open request within 10 seconds
                if (_openedContainers.ContainsKey(containerEntity.Id) && DateTime.Now.Subtract(_openedContainers[containerEntity.Id]).TotalSeconds < 10)
                    continue;

                // Don't even try to open a wreck if you are speed tanking and you aren't processing a loot action
                //if (Settings.Instance.SpeedTank == true && Cache.Instance.OpenWrecks == false)
                //    continue;

                // Don't even try to open a wreck if you are specified LootEverything as false and you aren't processing a loot action
                //      this is currently commented out as it would keep golems and other non-speed tanked ships from looting the field as they cleared
                //      missions, but NOT stick around after killing things to clear it ALL. Looteverything==false does NOT mean loot nothing
                //if (Settings.Instance.LootEverything == false && Cache.Instance.OpenWrecks == false)
                //    continue;

                // Open the container
                Logging.Log("Salvage: Opening container [" + containerEntity.Name + "][" + containerEntity.Id + "]");
                containerEntity.OpenCargo();
                _openedContainers[containerEntity.Id] = DateTime.Now;
                break;
            }
        }

        public void ProcessState()
        {
            // Nothing to salvage in stations
            if (Cache.Instance.InStation)
                return;

            var cargo = Cache.Instance.DirectEve.GetShipsCargo();
            switch (State)
            {
                case SalvageState.TargetWrecks:
                    TargetWrecks();

                    // Next state
                    State = SalvageState.LootWrecks;
                    break;

                case SalvageState.LootWrecks:
                    LootWrecks();

                    State = SalvageState.SalvageWrecks;
                    break;

                case SalvageState.SalvageWrecks:
                     ActivateTractorBeams();
                    ActivateSalvagers();

                    // Default action
                    State = SalvageState.TargetWrecks;
                    if (cargo.IsReady && cargo.Items.Any() && _nextSalvageAction < DateTime.Now)
                    {
                        // Check if there are actually duplicates
                        var duplicates = cargo.Items.Where(i => i.Quantity > 0).GroupBy(i => i.TypeId).Any(t => t.Count() > 1);
                        if (duplicates)
                            State = SalvageState.StackItems;
                        //else
                        //    _nextSalvageAction = DateTime.Now.AddMi((int)Time.SalvageDelayBetweenActions_miliseconds);
                    }
                    break;

                case SalvageState.StackItems:

                    if (cargo.IsReady)
                        cargo.StackAll();

                    _nextSalvageAction = DateTime.Now.AddSeconds((int)Time.SalvageStackItemsDelayBeforeResuming_seconds);
                    State = SalvageState.WaitForStacking;
                    break;

                case SalvageState.WaitForStacking:
                    if (_nextSalvageAction > DateTime.Now)
                        break;

                    if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        Logging.Log("Salvage: Done stacking");
                        State = SalvageState.TargetWrecks;
                        break;
                    }

                    if (DateTime.Now.Subtract(_nextSalvageAction).TotalSeconds > 120)
                    {
                        Logging.Log("Salvage: Stacking items timed out, clearing item locks");
                        Cache.Instance.DirectEve.UnlockItems();

                        Logging.Log("Salvage: Done stacking");
                        State = SalvageState.TargetWrecks;
                        break;
                    }
                    break;

                default:
                    // Unknown state, goto first state
                    State = SalvageState.TargetWrecks;
                    break;
            }
        }
    }
}