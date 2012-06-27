
namespace Questor.Modules.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.States;

    public class Grab
    {
        public int Item { get; set; }

        public int Unit { get; set; }

        public string Hangar { get; set; }

        private double freeCargoCapacity;

        private DateTime _lastAction;

        public void ProcessState()
        {
            if (!Cache.Instance.InStation)
                return;

            if (Cache.Instance.InSpace)
                return;

            if (DateTime.Now < Cache.Instance.LastInSpace.AddSeconds(20)) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return;

            DirectContainer _hangar = null;

            if (!Cache.Instance.OpenItemsHangar("Grab")) return;
            if (!Cache.Instance.OpenShipsHangar("Grab")) return;

            if ("Local Hangar" == Hangar)
                _hangar = Cache.Instance.ItemHangar;
            else if ("Ship Hangar" == Hangar)
                _hangar = Cache.Instance.ShipHangar;
            //else
                //_hangar = Cache.Instance.DirectEve.GetCorporationHangar(Hangar); //this needs to be fixed

            switch (_States.CurrentGrabState)
            {
                case GrabState.Idle:
                case GrabState.Done:
                    break;

                case GrabState.Begin:
                    _States.CurrentGrabState = GrabState.OpenItemHangar;
                    break;

                case GrabState.OpenItemHangar:

                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 2)
                        break;

                    if ("Local Hangar" == Hangar)
                    {
                        if (!Cache.Instance.OpenItemsHangar("Drop")) return;
                    }
                    else if ("Ship Hangar" == Hangar)
                    {
                        if (!Cache.Instance.OpenShipsHangar("Drop")) return;
                        
                        if (_hangar.Window == null || !_hangar.Window.IsReady)
                            break;
                    }
                    else if (Hangar != null)
                    {
                        if (_hangar != null && _hangar.Window == null)
                        {
                            // No, command it to open
                            //Cache.Instance.DirectEve.OpenCorporationHangar();
                            break;
                        }

                        if (_hangar != null && !_hangar.Window.IsReady)
                            break;
                    }

                    Logging.Log("Grab", "Opening Hangar", Logging.white);

                    _States.CurrentGrabState = GrabState.OpenCargo;

                    break;

                case GrabState.OpenCargo:

                    if (!Cache.Instance.OpenCargoHold("Grab")) break;

                    Logging.Log("Grab", "Opening Cargo Hold", Logging.white);

                    freeCargoCapacity = Cache.Instance.CargoHold.Capacity - Cache.Instance.CargoHold.UsedCapacity;

                    _States.CurrentGrabState = Item == 00 ? GrabState.AllItems : GrabState.MoveItems;

                    break;

                case GrabState.MoveItems:

                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 2)
                        break;
                    if (Unit == 00)
                    {
                        DirectItem GrabItem = _hangar.Items.FirstOrDefault(i => (i.TypeId == Item));
                        if (GrabItem != null)
                        {
                            double totalVolum = GrabItem.Quantity * GrabItem.Volume;
                            if (freeCargoCapacity >= totalVolum)
                            {
                                Cache.Instance.CargoHold.Add(GrabItem, GrabItem.Quantity);
                                freeCargoCapacity -= totalVolum;
                                Logging.Log("Grab", "Moving all the items", Logging.white);
                                _lastAction = DateTime.Now;
                                _States.CurrentGrabState = GrabState.WaitForItems;
                            }
                            else
                            {
                                _States.CurrentGrabState = GrabState.Done;
                                Logging.Log("Grab", "No load capacity", Logging.white);
                            }
                        }
                    }
                    else
                    {
                        DirectItem GrabItem = _hangar.Items.FirstOrDefault(i => (i.TypeId == Item));
                        if (GrabItem != null)
                        {
                            double totalVolum = Unit * GrabItem.Volume;
                            if (freeCargoCapacity >= totalVolum)
                            {
                                Cache.Instance.CargoHold.Add(GrabItem, Unit);
                                freeCargoCapacity -= totalVolum;
                                Logging.Log("Grab", "Moving item", Logging.white);
                                _lastAction = DateTime.Now;
                                _States.CurrentGrabState = GrabState.WaitForItems;
                            }
                            else
                            {
                                _States.CurrentGrabState = GrabState.Done;
                                Logging.Log("Grab", "No load capacity", Logging.white);
                            }
                        }
                    }

                    break;

                case GrabState.AllItems:

                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 2)
                        break;

                    List<DirectItem> AllItem = _hangar.Items;
                    if (AllItem != null)
                    {
                        foreach (DirectItem item in AllItem)
                        {
                            double totalVolum = item.Quantity * item.Volume;

                            if (freeCargoCapacity >= totalVolum)
                            {
                                Cache.Instance.CargoHold.Add(item);
                                freeCargoCapacity -= totalVolum;
                            }
                        }
                        Logging.Log("Grab", "Moving items", Logging.white);
                        _lastAction = DateTime.Now;
                        _States.CurrentGrabState = GrabState.WaitForItems;
                    }

                    break;

                case GrabState.WaitForItems:
                    // Wait 5 seconds after moving
                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 5)
                        break;

                    if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        Logging.Log("Grab", "Done", Logging.white);
                        _States.CurrentGrabState = GrabState.Done;
                        break;
                    }

                    break;
            }
        }
    }
}