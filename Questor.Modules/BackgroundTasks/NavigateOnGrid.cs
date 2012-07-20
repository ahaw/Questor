
namespace Questor.Modules.BackgroundTasks
{
    using System;
    //using System.Linq;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    //using global::Questor.Modules.States;
    //using System.Globalization;
    using System.Linq;
    using DirectEve;

    public class NavigateOnGrid
    {
        public static DateTime AvoidBumpingThingsTimeStamp = Cache.Instance.StartTime;
        public static int SafeDistanceFromStructureMultiplier = 1;
        public static bool AvoidBumpoingThingsWarningSent = false;
        
        public static void AvoidBumpingThings(EntityCache thisBigObject, string module)
        {
            //if It hasn't been at least 60 seconds since we last session changed do not do anything
            if (Cache.Instance.InStation || !Cache.Instance.InSpace || Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked || (Cache.Instance.InSpace && Cache.Instance.LastSessionChange.AddSeconds(60) < DateTime.Now))
                return;
            //
            // if we are "too close" to the bigObject move away... (is orbit the best thing to do here?)
            //
            if (Cache.Instance.ClosestStargate.Distance > 9000 || Cache.Instance.ClosestStation.Distance > 5000)
            {
                //EntityCache thisBigObject = Cache.Instance.BigObjects.FirstOrDefault();
                if (thisBigObject != null)
                {
                    if (thisBigObject.Distance >= (int)Distance.TooCloseToStructure)
                    {
                        //we are no longer "too close" and can proceed.
                        AvoidBumpingThingsTimeStamp = DateTime.Now;
                        SafeDistanceFromStructureMultiplier = 1;
                        AvoidBumpoingThingsWarningSent = false;
                    }
                    else
                    {
                        if (DateTime.Now > Cache.Instance.NextOrbit)
                        {
                            if (DateTime.Now > AvoidBumpingThingsTimeStamp.AddSeconds(30))
                            {
                                if (SafeDistanceFromStructureMultiplier <= 4)
                                {
                                    //
                                    // for simplicitys sake we reset this timestamp every 30 sec until the multiplier hits 5 then it should stay static until we arent "too close" anymore
                                    //
                                    AvoidBumpingThingsTimeStamp = DateTime.Now;
                                    SafeDistanceFromStructureMultiplier++;
                                }
                                if (DateTime.Now > AvoidBumpingThingsTimeStamp.AddMinutes(5) && !AvoidBumpoingThingsWarningSent)
                                {
                                    Logging.Log("NavigateOnGrid","We are stuck on a object and have been trying to orbit away from it for over 5 min", Logging.orange);
                                    AvoidBumpoingThingsWarningSent = true;
                                }
                                if (DateTime.Now > AvoidBumpingThingsTimeStamp.AddMinutes(15))
                                {
                                    Cache.Instance.CloseQuestorCMDLogoff = false;
                                    Cache.Instance.CloseQuestorCMDExitGame = true;
                                    Cache.Instance.ReasonToStopQuestor = "navigateOnGrid: We have been stuck on an object for over 15 min";
                                    Logging.Log("ReasonToStopQuestor", Cache.Instance.ReasonToStopQuestor, Logging.yellow);
                                    Cache.Instance.SessionState = "Quitting";
                                }
                            }
                            thisBigObject.Orbit((int)Distance.SafeDistancefromStructure * SafeDistanceFromStructureMultiplier);
                            Logging.Log(module, 
                                       ": initiating Orbit of [" + thisBigObject.Name +
                                          "] orbiting at [" + ((int)Distance.SafeDistancefromStructure * SafeDistanceFromStructureMultiplier) + "]", Logging.white);
                            Cache.Instance.NextOrbit = DateTime.Now.AddSeconds(Time.Instance.OrbitDelay_seconds);
                        }
                        return;
                        //we are still too close, do not continue through the rest until we are not "too close" anymore
                    }
                }
            }
        }

        public static void OrbitGateorTarget(EntityCache target, string module)
        {
            if (DateTime.Now > Cache.Instance.NextOrbit)
            {
                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "OrbitGateorTarget Started", Logging.white);
                if (Cache.Instance.OrbitDistance == 0)
                {
                    Cache.Instance.OrbitDistance = 2000;
                }
                
                if (target.Distance + (int)Cache.Instance.OrbitDistance < Cache.Instance.MaxRange)
                {
                    if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "OrbitGateorTarget Started", Logging.white);
                    //Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction] ,"StartOrbiting: Target in range");
                    if (!Cache.Instance.IsApproachingOrOrbiting)
                    {
                        Logging.Log("CombatMissionCtrl.NavigateIntoRange", "We are not approaching nor orbiting", Logging.teal);
                            if (Settings.Instance.OrbitStructure)
                            {
                        EntityCache structure = Cache.Instance.Entities.Where(i => i.Name.Contains("Gate")).OrderBy(t => t.Distance).OrderBy(t => t.Distance).FirstOrDefault();
                                if (structure != null)
                                {
                                    structure.Orbit((int)Cache.Instance.OrbitDistance);
                                    Logging.Log(module, "Initiating Orbit around Gate [" + structure.Name + "][ID: " + structure.Id + "]", Logging.teal);

                                }
                                else
                                {
                                    structure = Cache.Instance.Entities.Where(i => i.GroupId == (int)Group.LargeCollidableStructure).OrderBy(t => t.Distance).OrderBy(t => t.Distance).FirstOrDefault();
                                    if (structure != null)
                                    {
                                        structure.Orbit((int)Cache.Instance.OrbitDistance);
                                        Logging.Log(module, "Initiating Orbit around Beacon,wreck or LCS [" + structure.Name + "][ID: " + structure.Id + "]", Logging.teal);
                                    }
                                    else
                                    {
                                        if (Settings.Instance.OrbitStructure)
                                        {
                                            structure = Cache.Instance.Entities.Where(i => i.Name.Contains("Beacon")).OrderBy(t => t.Distance).OrderBy(t => t.Distance).FirstOrDefault();
                                            if (structure != null)
                                            {
                                                structure.Orbit((int)Cache.Instance.OrbitDistance);
                                                Logging.Log(module, "Initiating Orbit around Beacon or LCS [" + structure.Name + "][ID: " + structure.Id + "]", Logging.teal);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Logging.Log("CombatMissionCtrl.NavigateIntoRange", "OrbitStructure disabled", Logging.teal);
                            target.Orbit(Cache.Instance.OrbitDistance);
                            Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.Id + "]", Logging.teal);
                        }
                        Cache.Instance.NextOrbit = DateTime.Now.AddSeconds(Time.Instance.OrbitDelay_seconds);
                        return;
                    }
                }
                else
                {
                    Logging.Log(module, "Possible out of range. ignoring orbit around structure", Logging.teal);
                    target.Orbit(Cache.Instance.OrbitDistance);
                    Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.Id + "]", Logging.teal);
                    Cache.Instance.NextOrbit = DateTime.Now.AddSeconds(Time.Instance.OrbitDelay_seconds);
                    return;
                }
                return;
            }

        }

        public static void NavigateIntoRange(EntityCache target, string module)
        {
            if (Cache.Instance.InWarp || Cache.Instance.InStation)
                return;

            if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange Started", Logging.white);
                
            if (Cache.Instance.OrbitDistance != Settings.Instance.OrbitDistance)
            {
                if (Cache.Instance.OrbitDistance == 0)
                {
                    Cache.Instance.OrbitDistance = Settings.Instance.OrbitDistance;
                    Logging.Log("CombatMissionCtrl", "Using default orbit distance: " + Cache.Instance.OrbitDistance + " (as the custom one was 0)", Logging.teal);
                }
                //else
                //    Logging.Log("CombatMissionCtrl", "Using custom orbit distance: " + Cache.Instance.OrbitDistance, Logging.teal);
            }
            //if (Cache.Instance.OrbitDistance != 0)
            //    Logging.Log("CombatMissionCtrl", "Orbit Distance is set to: " + (Cache.Instance.OrbitDistance / 1000).ToString(CultureInfo.InvariantCulture) + "k", Logging.teal);

            NavigateOnGrid.AvoidBumpingThings(Cache.Instance.BigObjectsandGates.FirstOrDefault(), "NavigateOnGrid: NavigateIntoRange");

            if (Settings.Instance.SpeedTank)
            {   
                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: speedtank: orbitdistance is [" + Cache.Instance.OrbitDistance + "]", Logging.white);
                OrbitGateorTarget(target, module);
            }
            else //if we aren't speed tanking then check optimalrange setting, if that isn't set use the less of targeting range and weapons range to dictate engagement range
            {
                if (DateTime.Now > Cache.Instance.NextApproachAction)
                {
                    //if optimalrange is set - use it to determine engagement range
                    if (Settings.Instance.OptimalRange != 0)
                    {
                        if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: OptimalRange [ " + Settings.Instance.OptimalRange + "] Current Distance to [" + target.Name + "] is [" + target.Distance + "]", Logging.white);

                        if (target.Distance > Settings.Instance.OptimalRange + (int)Distance.OptimalRangeCushion && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                        {
                            if (target.IsNPCFrigate)
                            {
                                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.white);
                                OrbitGateorTarget(target, module);
                                return;
                            }
                            target.Approach(Settings.Instance.OptimalRange);
                            Logging.Log(module, "Using Optimal Range: Approaching target [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.teal);
                        }
                        //I think when approach distance will be reached ship will be stopped so this is not needed
                        if (target.Distance <= Settings.Instance.OptimalRange && Cache.Instance.Approaching != null)
                        {
                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                            Cache.Instance.Approaching = null;
                            Logging.Log(module, "Using Optimal Range: Stop ship, target at [" + Math.Round(target.Distance / 1000, 0) + "k away] is inside optimal", Logging.teal);
                        }
                    }
                    //if optimalrange is not set use MaxRange (shorter of weapons range and targeting range)
                    else
                    {
                        if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: using MaxRange [" + Cache.Instance.MaxRange + "] target is [" + target.Name + "][" + target.Distance + "]", Logging.white);

                        if (target.Distance > Cache.Instance.MaxRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                        {
                            if (target.IsNPCFrigate)
                            {
                                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.white);
                                OrbitGateorTarget(target, module);
                                return;
                            }
                            target.Approach((int)(Cache.Instance.WeaponRange * 0.8d));
                            Logging.Log(module, "Using Weapons Range: Approaching target [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.teal);
                        }
                        //I think when approach distance will be reached ship will be stopped so this is not needed
                        if (target.Distance <= Cache.Instance.MaxRange && Cache.Instance.Approaching != null)
                        {
                            if (target.IsNPCFrigate)
                            {
                                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.white);
                                OrbitGateorTarget(target, module);
                                return;
                            }
                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                            Cache.Instance.Approaching = null;
                            Logging.Log(module, "Using Weapons Range: Stop ship, target is in orbit range", Logging.teal);
                        }
                    }
                    Cache.Instance.NextApproachAction = DateTime.Now.AddSeconds(Time.Instance.ApproachDelay_seconds);
                    return;
                }
            }
        }

        public static void NavigateToObject(EntityCache target, string module)  //this needs to accept a distance parameter....
        {
            if (Settings.Instance.SpeedTank)
            {   //this should be only executed when no specific actions
                if (DateTime.Now > Cache.Instance.NextOrbit)
                {
                    if (target.Distance + (int)Cache.Instance.OrbitDistance < Cache.Instance.MaxRange)
                    {
                        Logging.Log(module, "StartOrbiting: Target in range", Logging.teal);
                        if (!Cache.Instance.IsApproachingOrOrbiting)
                        {
                            Logging.Log("CombatMissionCtrl.NavigateToObject", "We are not approaching nor orbiting", Logging.teal);
                            const bool orbitStructure = true;
                            var structure = Cache.Instance.Entities.Where(i => i.GroupId == (int)Group.LargeCollidableStructure || i.Name.Contains("Gate") || i.Name.Contains("Beacon")).OrderBy(t => t.Distance).OrderBy(t => t.Distance).FirstOrDefault();

                            if (orbitStructure && structure != null)
                            {
                                structure.Orbit((int)Cache.Instance.OrbitDistance);
                                Logging.Log(module, "Initiating Orbit [" + structure.Name + "][ID: " + structure.Id + "]", Logging.teal);
                            }
                            else
                            {
                                target.Orbit(Cache.Instance.OrbitDistance);
                                Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.Id + "]", Logging.teal);
                            }
                            Cache.Instance.NextOrbit = DateTime.Now.AddSeconds(Time.Instance.OrbitDelay_seconds);
                            return;
                        }
                    }
                    else
                    {
                        Logging.Log(module, "Possible out of range. ignoring orbit around structure", Logging.teal);
                        target.Orbit(Cache.Instance.OrbitDistance);
                        Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.Id + "]", Logging.teal);
                        Cache.Instance.NextOrbit = DateTime.Now.AddSeconds(Time.Instance.OrbitDelay_seconds);
                        return;
                    }
                }
            }
            else //if we aren't speed tanking then check optimalrange setting, if that isn't set use the less of targeting range and weapons range to dictate engagement range
            {
                if (DateTime.Now > Cache.Instance.NextApproachAction)
                {
                    //if optimalrange is set - use it to determine engagement range
                    //
                    // this assumes that both optimal range and missile boats both want to be within 5k of the object they asked us to navigate to
                    //
                    if (target.Distance > Cache.Instance.MaxRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                    {
                        target.Approach((int)(Distance.SafeDistancefromStructure));
                        Cache.Instance.NextApproachAction = DateTime.Now.AddSeconds(Time.Instance.ApproachDelay_seconds);
                        Logging.Log(module, "Using SafeDistanceFromStructure: Approaching target [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.teal);
                    }
                    return;
                }
            }
        }
    }
}