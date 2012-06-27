
namespace Questor.Modules.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.States;

    public class Drop
    {
        public int Item { get; set; }

        public int Unit { get; set; }

        public string Hangar { get; set; }

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

            if (!Cache.Instance.OpenItemsHangar("Drop")) return;
            if (!Cache.Instance.OpenShipsHangar("Drop")) return;

            if ("Local Hangar" == Hangar)
                _hangar = Cache.Instance.ItemHangar;
            else if ("Ship Hangar" == Hangar)
                _hangar = Cache.Instance.ShipHangar;
            //else
                //_hangar = Cache.Instance.DirectEve.GetCorporationHangar(Hangar); //this needs to be fixed

            switch (_States.CurrentDropState)
            {
                case DropState.Idle:
                case DropState.Done:
                    break;

                case DropState.Begin:
                    _States.CurrentDropState = DropState.OpenItemHangar;
                    break;

                case DropState.OpenItemHangar:

                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 2)
                        break;

                    if ("Local Hangar" == Hangar)
                    {
                        if (!Cache.Instance.OpenItemsHangar("Drop")) return;
                    }
                    else if ("Ship Hangar" == Hangar)
                    {
                        if (!Cache.Instance.OpenShipsHangar("Drop")) return;
                    }
                    else
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

                    Logging.Log("Drop", "Opening Hangar", Logging.white);
                    _States.CurrentDropState = DropState.OpenCargo;
                    break;

                case DropState.OpenCargo:

                    if (!Cache.Instance.OpenCargoHold("Drop")) break;

                    Logging.Log("Drop", "Opening Cargo Hold", Logging.white);
                    _States.CurrentDropState = Item == 00 ? DropState.AllItems : DropState.MoveItems;

                    break;

                case DropState.MoveItems:

                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 2)
                        break;

                    if (Unit == 00)
                    {
                        DirectItem DropItem = Cache.Instance.CargoHold.Items.FirstOrDefault(i => (i.TypeId == Item));
                        if (DropItem != null)
                        {
                            if (_hangar != null) _hangar.Add(DropItem, DropItem.Quantity);
                            Logging.Log("Drop", "Moving all the items", Logging.white);
                            _lastAction = DateTime.Now;
                            _States.CurrentDropState = DropState.WaitForMove;
                        }
                    }
                    else
                    {
                        DirectItem DropItem = Cache.Instance.CargoHold.Items.FirstOrDefault(i => (i.TypeId == Item));
                        if (DropItem != null)
                        {
                            if (_hangar != null) _hangar.Add(DropItem, Unit);
                            Logging.Log("Drop", "Moving item", Logging.white);
                            _lastAction = DateTime.Now;
                            _States.CurrentDropState = DropState.WaitForMove;
                        }
                    }

                    break;

                case DropState.AllItems:

                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 2)
                        break;

                    List<DirectItem> AllItem = Cache.Instance.CargoHold.Items;
                    if (AllItem != null)
                    {
                        if (_hangar != null) _hangar.Add(AllItem);
                        Logging.Log("Drop", "Moving item", Logging.white);
                        _lastAction = DateTime.Now;
                        _States.CurrentDropState = DropState.WaitForMove;
                    }

                    break;

                case DropState.WaitForMove:
                    if (Cache.Instance.CargoHold.Items.Count != 0)
                    {
                        _lastAction = DateTime.Now;
                        break;
                    }

                    // Wait 5 seconds after moving
                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 5)
                        break;

                    if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        _States.CurrentDropState = DropState.StackItemsHangar;
                        break;
                    }

                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds > 120)
                    {
                        Logging.Log("Drop", "Moving items timed out, clearing item locks", Logging.white);
                        Cache.Instance.DirectEve.UnlockItems();

                        _States.CurrentDropState = DropState.StackItemsHangar;
                        break;
                    }
                    break;

                case DropState.StackItemsHangar:
                    // Do not stack until 5 seconds after the cargo has cleared
                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 5)
                        break;

                    // Stack everything
                    if (_hangar != null && _hangar.Window.IsReady)
                    {
                        Logging.Log("Drop", "Stacking items", Logging.white);
                        _hangar.StackAll();
                        _lastAction = DateTime.Now;
                        _States.CurrentDropState = DropState.WaitForStacking;
                    }
                    break;

                case DropState.WaitForStacking:
                    // Wait 5 seconds after stacking
                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds < 5)
                        break;

                    if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        Logging.Log("Drop", "Done", Logging.white);
                        _States.CurrentDropState = DropState.Done;
                        break;
                    }

                    if (DateTime.Now.Subtract(_lastAction).TotalSeconds > 120)
                    {
                        Logging.Log("Drop", "Stacking items timed out, clearing item locks", Logging.white);
                        Cache.Instance.DirectEve.UnlockItems();

                        Logging.Log("Drop", "Done", Logging.white);
                        _States.CurrentDropState = DropState.Done;
                        break;
                    }
                    break;
            }
        }
    }
}