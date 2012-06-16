
using System.Linq;
using DirectEve;

namespace Questor.Modules.BackgroundTasks
{
    using System;
    //using System.Linq;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;

    public class NavigateOnGrid
    {
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
                    }
                    else
                    {
                        if (DateTime.Now > Cache.Instance.NextOrbit)
                        {
                            thisBigObject.Orbit((int)Distance.SafeDistancefromStructure);
                            Logging.Log(module, 
                                       ": initiating Orbit of [" + thisBigObject.Name +
                                          "] orbiting at [" + Distance.SafeDistancefromStructure + "]", Logging.white);
                            Cache.Instance.NextOrbit = DateTime.Now.AddSeconds((int)Time.OrbitDelay_seconds);
                        }
                        return;
                        //we are still too close, do not continue through the rest until we are not "too close" anymore
                    }
                }
            }
        }

        public static void NavigateIntoRange(EntityCache target,string module)
        {
            if (Cache.Instance.InWarp || Cache.Instance.InStation)
                return;

            NavigateOnGrid.AvoidBumpingThings(Cache.Instance.BigObjectsandGates.FirstOrDefault(), "NavigateOnGrid: NavigateIntoRange");

            if (Settings.Instance.SpeedTank)
            {   //this should be only executed when no specific actions
                if (DateTime.Now > Cache.Instance.NextOrbit)
                {
                    if (target.Distance + (int)Cache.Instance.OrbitDistance < Cache.Instance.MaxRange)
                    {
                        //Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction] ,"StartOrbiting: Target in range");
                        if (!Cache.Instance.IsApproachingOrOrbiting)
                        {
                            Logging.Log("CombatMissionCtrl.NavigateIntoRange", "We are not approaching nor orbiting", Logging.teal);

                            EntityCache structure = Cache.Instance.Entities.Where(i => i.Name.Contains("Gate")).OrderBy(t => t.Distance).OrderBy(t => t.Distance).FirstOrDefault();

                            if (Settings.Instance.OrbitStructure && structure != null)
                            {
                                structure.Orbit((int)Cache.Instance.OrbitDistance);
                                Logging.Log(module, "Initiating Orbit [" + structure.Name + "][ID: " + structure.Id + "]", Logging.teal);
                            }
                            else
                            {
                                target.Orbit(Cache.Instance.OrbitDistance);
                                Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.Id + "]", Logging.teal);
                            }
                            Cache.Instance.NextOrbit = DateTime.Now.AddSeconds((int)Time.OrbitDelay_seconds);
                            return;
                        }
                    }
                    else
                    {
                        Logging.Log(module, "Possible out of range. ignoring orbit around structure", Logging.teal);
                        target.Orbit(Cache.Instance.OrbitDistance);
                        Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.Id + "]", Logging.teal);
                        Cache.Instance.NextOrbit = DateTime.Now.AddSeconds((int)Time.OrbitDelay_seconds);
                        return;
                    }
                }
            }
            else //if we aren't speed tanking then check optimalrange setting, if that isn't set use the less of targeting range and weapons range to dictate engagement range
            {
                if (DateTime.Now > Cache.Instance.NextApproachAction)
                {
                    //if optimalrange is set - use it to determine engagement range
                    if (Settings.Instance.OptimalRange != 0)
                    {
                        if (target.Distance > Settings.Instance.OptimalRange + (int)Distance.OptimalRangeCushion && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                        {
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
                        if (target.Distance > Cache.Instance.MaxRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                        {
                            target.Approach((int)(Cache.Instance.WeaponRange * 0.8d));
                            Logging.Log(module, "Using Weapons Range: Approaching target [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.teal);
                        }
                        //I think when approach distance will be reached ship will be stopped so this is not needed
                        if (target.Distance <= Cache.Instance.MaxRange && Cache.Instance.Approaching != null)
                        {
                            Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                            Cache.Instance.Approaching = null;
                            Logging.Log(module, "Using Weapons Range: Stop ship, target is in orbit range", Logging.teal);
                        }
                    }
                    Cache.Instance.NextApproachAction = DateTime.Now.AddSeconds((int)Time.ApproachDelay_seconds);
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
                            Cache.Instance.NextOrbit = DateTime.Now.AddSeconds((int)Time.OrbitDelay_seconds);
                            return;
                        }
                    }
                    else
                    {
                        Logging.Log(module, "Possible out of range. ignoring orbit around structure", Logging.teal);
                        target.Orbit(Cache.Instance.OrbitDistance);
                        Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + target.Id + "]", Logging.teal);
                        Cache.Instance.NextOrbit = DateTime.Now.AddSeconds((int)Time.OrbitDelay_seconds);
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
                        Cache.Instance.NextApproachAction = DateTime.Now.AddSeconds((int)Time.ApproachDelay_seconds);
                        Logging.Log(module, "Using SafeDistanceFromStructure: Approaching target [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.teal);
                    }
                    return;
                }
            }
        }
    }
}