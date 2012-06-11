//------------------------------------------------------------------------------
//  <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//    Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//    Please look in the accompanying license.htm file for the license that
//    applies to this source code. (a copy can also be found at:
//    http://www.thehackerwithin.com/license.htm)
//  </copyright>
//-------------------------------------------------------------------------------

#define manual

namespace ValueDump
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows.Forms;
    using System.Xml.Linq;
    using System.IO;
    using DirectEve;
    using Valuedump;
    using Questor.Modules.Caching;
    using Questor.Modules.Lookup;
    using Questor.Modules.Logging;

    public partial class ValueDumpUI : Form
    {
        private DateTime _lastPulse;

        private Dictionary<int, InvTypeMarket> InvTypesById { get; set; }

        private List<ItemCacheMarket> Items { get; set; }

        private List<ItemCacheMarket> ItemsToSell { get; set; }

        private List<ItemCacheMarket> ItemsToRefine { get; set; }

        private ValueDumpState State { get; set; }

        private DirectEve _directEve { get; set; }

        public string CharacterName { get; set; }

        public string InvTypesPath
        {
            get
            {
                return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\InvTypes.xml";
            }
        }

        public ValueDumpUI()
        {
            Logging.Log("ValueDumpUI","Starting ValueDump",Logging.orange);
            InitializeComponent();

            InvTypesById = new Dictionary<int, InvTypeMarket>();
            XDocument invTypes = XDocument.Load(InvTypesPath);
            if (invTypes.Root != null)
                foreach (XElement element in invTypes.Root.Elements("invtype"))
                    InvTypesById.Add((int)element.Attribute("id"), new InvTypeMarket(element));

            Items = new List<ItemCacheMarket>();
            ItemsToSell = new List<ItemCacheMarket>();
            ItemsToRefine = new List<ItemCacheMarket>();

            _directEve = new DirectEve();
            Cache.Instance.DirectEve = _directEve;
            _directEve.OnFrame += OnFrame;
        }

        private InvTypeMarket _currentMineral;
        private ItemCacheMarket _currentItem;
        private DateTime _lastExecute = DateTime.MinValue;

        private void OnFrame(object sender, EventArgs e)
        {
            Cache.Instance.LastFrame = DateTime.Now;
            // Only pulse state changes every .5s
            if (DateTime.Now.Subtract(_lastPulse).TotalMilliseconds < (int)Time.ValueDumpPulse_milliseconds) //default: 500ms
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

            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            // Update settings (settings only load if character name changed)
            if (!Settings.Instance.Defaultsettingsloaded)
            {
                Settings.Instance.LoadSettings();
            }
            
            if (DateTime.Now.Subtract(Cache.Instance.LastupdateofSessionRunningTime).TotalSeconds <
                (int)Time.SessionRunningTimeUpdate_seconds)
            {
                Cache.Instance.SessionRunningTime =
                    (int)DateTime.Now.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes;
                Cache.Instance.LastupdateofSessionRunningTime = DateTime.Now;
            }

            if (State == ValueDumpState.Idle)
                return;

            XDocument invIgnore = XDocument.Load(Settings.Instance.Path + "\\InvIgnore.xml"); //items to ignore
            DirectMarketWindow marketWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketWindow>().FirstOrDefault();
            DirectMarketActionWindow sellWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketActionWindow>().FirstOrDefault(w => w.IsSellAction);
            DirectReprocessingWindow reprorcessingWindow = Cache.Instance.DirectEve.Windows.OfType<DirectReprocessingWindow>().FirstOrDefault();
            //bool block;

            switch (State)
            {
                case ValueDumpState.CheckMineralPrices:
                    if (RefineCheckBox.Checked)
                        _currentMineral = InvTypesById.Values.FirstOrDefault(i => i.ReprocessValue.HasValue && i.LastUpdate < DateTime.Now.AddDays(-7));
                    else
                        _currentMineral = InvTypesById.Values.FirstOrDefault(i => i.Id != 27029 && i.GroupId == 18 && i.LastUpdate < DateTime.Now.AddHours(-4));
                    //_currentMineral = InvTypesById.Values.FirstOrDefault(i => i.Id != 27029 && i.GroupId == 18 && i.LastUpdate < DateTime.Now.AddMinutes(-1));
                    //_currentMineral = InvTypesById.Values.FirstOrDefault(i => i.Id == 20236 && i.LastUpdate < DateTime.Now.AddMinutes(-1));
                    if (_currentMineral == null)
                    {
                        if (DateTime.Now.Subtract(_lastExecute).TotalSeconds > (int)Time.Marketlookupdelay_seconds)
                        {
                            State = ValueDumpState.SaveMineralPrices;
                            if (marketWindow != null)
                                marketWindow.Close();
                        }
                    }
                    else
                    {
                        //State = ValueDumpState.GetMineralPrice;
                        if (marketWindow == null)
                        {
                            if (DateTime.Now.Subtract(_lastExecute).TotalSeconds > (int)Time.Marketlookupdelay_seconds)
                            {
                                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenMarket);
                                _lastExecute = DateTime.Now;
                            }
                            return;
                        }

                        if (!marketWindow.IsReady)
                            return;

                        if (marketWindow.DetailTypeId != _currentMineral.Id)
                        {
                            if (DateTime.Now.Subtract(_lastExecute).TotalSeconds < (int)Time.Marketlookupdelay_seconds)
                                return;

                            Logging.Log("ValuedumpUI", "Loading orders for " + _currentMineral.Name, Logging.white);

                            marketWindow.LoadTypeId(_currentMineral.Id);
                            _lastExecute = DateTime.Now;
                            return;
                        }

                        if (!marketWindow.BuyOrders.Any(o => o.StationId == Cache.Instance.DirectEve.Session.StationId))
                        {
                            _currentMineral.LastUpdate = DateTime.Now;

                            Logging.Log("ValuedumpUI", "No buy orders found for " + _currentMineral.Name, Logging.white);
                            State = ValueDumpState.CheckMineralPrices;
                        }

                        // Take top 5 orders, average the buy price and consider that median-buy (it's not really median buy but its what we want)
                        //_currentMineral.MedianBuy = marketWindow.BuyOrders.Where(o => o.StationId == DirectEve.Session.StationId).OrderByDescending(o => o.Price).Take(5).Average(o => o.Price);

                        // Take top 1% orders and count median-buy price (no botter covers more than 1% Jita orders anyway)
                        List<DirectOrder> orders = marketWindow.BuyOrders.Where(o => o.StationId == Cache.Instance.DirectEve.Session.StationId && o.MinimumVolume == 1).OrderByDescending(o => o.Price).ToList();
                        double totalAmount = orders.Sum(o => (double)o.VolumeRemaining);
                        double amount = 0, value = 0, count = 0;
                        for (int i = 0; i < orders.Count(); i++)
                        {
                            amount += orders[i].VolumeRemaining;
                            value += orders[i].VolumeRemaining * orders[i].Price;
                            count++;
                            //Logging.Log(_currentMineral.Name + " " + count + ": " + orders[i].VolumeRemaining.ToString("#,##0") + " items @ " + orders[i].Price);
                            if (amount / totalAmount > 0.01)
                                break;
                        }
                        _currentMineral.MedianBuy = value / amount;
                        Logging.Log("ValuedumpUI", "Average buy price for " + _currentMineral.Name + " is " + _currentMineral.MedianBuy.Value.ToString("#,##0.00") + " (" + count + " / " + orders.Count() + " orders, " + amount.ToString("#,##0") + " / " + totalAmount.ToString("#,##0") + " items)", Logging.white);

                        if (!marketWindow.SellOrders.Any(o => o.StationId == Cache.Instance.DirectEve.Session.StationId))
                        {
                            _currentMineral.LastUpdate = DateTime.Now;

                            Logging.Log("ValuedumpUI", "No sell orders found for " + _currentMineral.Name, Logging.white);
                            State = ValueDumpState.CheckMineralPrices;
                        }

                        // Take top 1% orders and count median-sell price
                        orders = marketWindow.SellOrders.Where(o => o.StationId == Cache.Instance.DirectEve.Session.StationId).OrderBy(o => o.Price).ToList();
                        totalAmount = orders.Sum(o => (double)o.VolumeRemaining);
                        amount = 0; value = 0; count = 0;
                        for (int i = 0; i < orders.Count(); i++)
                        {
                            amount += orders[i].VolumeRemaining;
                            value += orders[i].VolumeRemaining * orders[i].Price;
                            count++;
                            //Logging.Log(_currentMineral.Name + " " + count + ": " + orders[i].VolumeRemaining.ToString("#,##0") + " items @ " + orders[i].Price);
                            if (amount / totalAmount > 0.01)
                                break;
                        }
                        _currentMineral.MedianSell = value / amount - 0.01;
                        Logging.Log("ValuedumpUI", "Average sell price for " + _currentMineral.Name + " is " + _currentMineral.MedianSell.Value.ToString("#,##0.00") + " (" + count + " / " + orders.Count() + " orders, " + amount.ToString("#,##0") + " / " + totalAmount.ToString("#,##0") + " items)", Logging.white);

                        if (_currentMineral.MedianSell.HasValue && !double.IsNaN(_currentMineral.MedianSell.Value))
                            _currentMineral.MedianAll = _currentMineral.MedianSell;
                        else if (_currentMineral.MedianBuy.HasValue && !double.IsNaN(_currentMineral.MedianBuy.Value))
                            _currentMineral.MedianAll = _currentMineral.MedianBuy;
                        _currentMineral.LastUpdate = DateTime.Now;
                        //State = ValueDumpState.CheckMineralPrices;
                    }
                    break;

                case ValueDumpState.GetMineralPrice:
                    break;

                case ValueDumpState.SaveMineralPrices:
                    Logging.Log("ValuedumpUI", "Updating reprocess prices", Logging.white);

                    // a quick price check table
                    Dictionary<string, double> mineralPrices = new Dictionary<string, double>();
                    foreach (InvTypeMarket i in InvTypesById.Values)
                        if (InvTypeMarket.Minerals.Contains(i.Name))
#if manual
                            mineralPrices.Add(i.Name, i.MedianSell ?? 0);
#else
                            MineralPrices.Add(i.Name, i.MedianBuy ?? 0);
#endif

                    double temp;
                    foreach (InvTypeMarket i in InvTypesById.Values)
                    {
                        temp = 0;
                        foreach (string m in InvTypeMarket.Minerals)
                            if (i.Reprocess[m].HasValue && i.Reprocess[m] > 0)
                            {
                                var d = i.Reprocess[m];
                                if (d != null) temp += d.Value * mineralPrices[m];
                            }
                        if (temp > 0)
                            i.ReprocessValue = temp;
                        else
                            i.ReprocessValue = null;
                    }

                    Logging.Log("ValuedumpUI", "Saving InvTypes.xml", Logging.white);

                    XDocument xdoc = new XDocument(new XElement("invtypes"));
                    foreach (InvTypeMarket type in InvTypesById.Values.OrderBy(i => i.Id))
                        xdoc.Root.Add(type.Save());
                    xdoc.Save(InvTypesPath);

                    State = ValueDumpState.Idle;
                    break;

                case ValueDumpState.GetItems:
                    if (!Cache.Instance.OpenItemsHangar("ValueDump")) break;
                    Logging.Log("ValueDumpUI", "Loading hangar items", Logging.white);

                    // Clear out the old
                    Items.Clear();
                    List<DirectItem> hangarItems = Cache.Instance.ItemHangar.Items;
                    if (hangarItems != null)
                        Items.AddRange(hangarItems.Where(i => i.ItemId > 0 && i.Quantity > 0).Select(i => new ItemCacheMarket(i, RefineCheckBox.Checked)));

                    State = ValueDumpState.UpdatePrices;
                    break;

                case ValueDumpState.UpdatePrices:
                    bool updated = false;

                    foreach (ItemCacheMarket item in Items)
                    {
                        InvTypeMarket invType;
                        if (!InvTypesById.TryGetValue(item.TypeId, out invType))
                        {
                            Logging.Log("Valuedump","Unknown TypeId " + item.TypeId + " for " + item.Name + ", adding to the list",Logging.orange);
                            invType = new InvTypeMarket(item);
                            InvTypesById.Add(item.TypeId, invType);
                            updated = true;
                            continue;
                        }
                        item.InvType = invType;

                        bool updItem = false;
                        foreach (ItemCacheMarket material in item.RefineOutput)
                        {
                            if (!InvTypesById.TryGetValue(material.TypeId, out invType))
                            {
                                Logging.Log("Valuedump","Unknown TypeId " + material.TypeId + " for " + material.Name,Logging.white);
                                continue;
                            }
                            material.InvType = invType;

                            double matsPerItem = (double)material.Quantity / item.PortionSize;
                            bool exists = InvTypesById[(int)item.TypeId].Reprocess[material.Name].HasValue;
                            if ((!exists && matsPerItem > 0) || (exists && InvTypesById[(int)item.TypeId].Reprocess[material.Name] != matsPerItem))
                            {
                                if (exists)
                                    Logging.Log("ValueDumpUI",
                                        Logging.orange + " [" + Logging.white + 
                                        item.Name +
                                        Logging.orange + "][" + Logging.white + 
                                        material.Name +
                                        Logging.orange + "] old value: [" + Logging.white + 
                                        InvTypesById[(int)item.TypeId].Reprocess[material.Name] + ", new value: " +
                                        Logging.orange + "[" + Logging.white + matsPerItem +
                                        Logging.orange + "]", Logging.white);
                                InvTypesById[(int)item.TypeId].Reprocess[material.Name] = matsPerItem;
                                updItem = true;
                            }
                        }

                        if (updItem)
                            Logging.Log("ValueDumpUI", "Updated [" + item.Name + "] refine materials", Logging.white);
                        updated |= updItem;
                    }

                    if (updated)
                        State = ValueDumpState.SaveMineralPrices;
                    else
                        State = ValueDumpState.Idle;

                    if (cbxSell.Checked)
                    {
                        // Copy the items to sell list
                        ItemsToSell.Clear();
                        ItemsToRefine.Clear();
                        if (cbxUndersell.Checked)
#if manual
                            ItemsToSell.AddRange(Items.Where(i => i.InvType != null && i.MarketGroupId > 0));
#else
                            ItemsToSell.AddRange(Items.Where(i => i.InvType != null && i.MarketGroupId > 0));
#endif
                        else
#if manual
                            ItemsToSell.AddRange(Items.Where(i => i.InvType != null && i.MarketGroupId > 0 && i.InvType.MedianBuy.HasValue));
#else
                            ItemsToSell.AddRange(Items.Where(i => i.InvType != null && i.MarketGroupId > 0 && i.InvType.MedianBuy.HasValue));
#endif
                        State = ValueDumpState.NextItem;
                    }
                    break;

                case ValueDumpState.NextItem:
                    if (ItemsToSell.Count == 0)
                    {
                        if (ItemsToRefine.Count != 0)
                            State = ValueDumpState.RefineItems;
                        else
                            State = ValueDumpState.Idle;
                        break;
                    }

                    Logging.Log("ValueDumpUI", ItemsToSell.Count + " items left to sell", Logging.white);

                    _currentItem = ItemsToSell[0];
                    ItemsToSell.RemoveAt(0);

                    // Do not sell containers
                    if (_currentItem.GroupId == 448)
                    {
                        Logging.Log("ValueDumpUI", "Skipping " + _currentItem.Name, Logging.white);
                        break;
                    }

                    State = ValueDumpState.StartQuickSell;
                    break;

                case ValueDumpState.StartQuickSell:
                    if (DateTime.Now.Subtract(_lastExecute).TotalSeconds < (int)Time.Marketsellorderdelay_seconds)
                        break;
                    _lastExecute = DateTime.Now;

                    DirectItem directItem = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.ItemId == _currentItem.Id);
                    if (directItem == null)
                    {
                        Logging.Log("ValueDumpUI", "Item " + _currentItem.Name + " no longer exists in the hanger", Logging.white);
                        break;
                    }

                    // Update Quantity
                    _currentItem.QuantitySold = _currentItem.Quantity - directItem.Quantity;

                    Logging.Log("ValueDumpUI", "Starting QuickSell for " + _currentItem.Name, Logging.white);
                    if (!directItem.QuickSell())
                    {
                        _lastExecute = DateTime.Now.AddSeconds(-5);

                        Logging.Log("ValueDumpUI", "QuickSell failed for " + _currentItem.Name + ", retrying in 5 seconds", Logging.white);
                        break;
                    }

                    State = ValueDumpState.WaitForSellWindow;
                    break;

                case ValueDumpState.WaitForSellWindow:
                    if (sellWindow == null || !sellWindow.IsReady || sellWindow.Item.ItemId != _currentItem.Id)
                        break;

                    // Mark as new execution
                    _lastExecute = DateTime.Now;

                    Logging.Log("ValueDumpUI", "Inspecting sell order for " + _currentItem.Name, Logging.white);
                    State = ValueDumpState.InspectOrder;
                    break;

                case ValueDumpState.InspectOrder:
                    // Let the order window stay open for 2 seconds
                    if (DateTime.Now.Subtract(_lastExecute).TotalSeconds < (int)Time.Marketbuyorderdelay_seconds)
                        break;

                    if (sellWindow != null && (!sellWindow.OrderId.HasValue || !sellWindow.Price.HasValue || !sellWindow.RemainingVolume.HasValue))
                    {
                        Logging.Log("ValueDumpUI", "No order available for " + _currentItem.Name, Logging.white);

                        sellWindow.Cancel();
                        State = ValueDumpState.WaitingToFinishQuickSell;
                        break;
                    }

                    if (sellWindow != null)
                    {
                        double price = sellWindow.Price.Value;
                        int quantity = (int)Math.Min(_currentItem.Quantity - _currentItem.QuantitySold, sellWindow.RemainingVolume.Value);
                        double totalPrice = quantity * price;

                        string otherPrices = " ";
                        if (_currentItem.InvType.MedianBuy.HasValue)
                            otherPrices += "[Median buy price: " + (_currentItem.InvType.MedianBuy.Value * quantity).ToString("#,##0.00") + "]";
                        else
                            otherPrices += "[No median buy price]";

                        if (RefineCheckBox.Checked)
                        {
                            int portions = quantity / _currentItem.PortionSize;
                            double refinePrice = _currentItem.RefineOutput.Any() ? _currentItem.RefineOutput.Sum(m => m.Quantity * m.InvType.MedianBuy ?? 0) * portions : 0;
                            refinePrice *= (double)RefineEfficiencyInput.Value / 100;

                            otherPrices += "[Refine price: " + refinePrice.ToString("#,##0.00") + "]";

                            if (refinePrice > totalPrice)
                            {
                                Logging.Log("ValueDumpUI", "Refining gives a better price for item " + _currentItem.Name + 
                                    Logging.orange + " [" + Logging.white + 
                                    "Refine price: " + refinePrice.ToString("#,##0.00") + 
                                    Logging.orange + "][" + Logging.white + 
                                    "Sell price: " + totalPrice.ToString("#,##0.00") +
                                    Logging.orange + "]", Logging.white);

                                // Add it to the refine list
                                ItemsToRefine.Add(_currentItem);

                                sellWindow.Cancel();
                                State = ValueDumpState.WaitingToFinishQuickSell;
                                break;
                            }
                        }

                        if (!cbxUndersell.Checked)
                        {
                            if (!_currentItem.InvType.MedianBuy.HasValue)
                            {
                                Logging.Log("ValueDumpUI", "No historical price available for " + _currentItem.Name, Logging.white);

                                sellWindow.Cancel();
                                State = ValueDumpState.WaitingToFinishQuickSell;
                                break;
                            }

                            double perc = price / _currentItem.InvType.MedianBuy.Value;
                            double total = _currentItem.InvType.MedianBuy.Value * _currentItem.Quantity;
                            // If percentage < 85% and total price > 1m isk then skip this item (we don't undersell)
                            if (perc < 0.85 && total > 1000000)
                            {
                                Logging.Log("ValueDumpUI", "Not underselling item " + _currentItem.Name +
                                    Logging.orange + " [" + Logging.white +  
                                    "Median buy price: " + _currentItem.InvType.MedianBuy.Value.ToString("#,##0.00") + 
                                    Logging.orange  + "][" + Logging.white + 
                                    "Sell price: " + price.ToString("#,##0.00") +
                                    Logging.orange + "][" + Logging.white +
                                    perc.ToString("0%") + 
                                    Logging.orange + "]", Logging.white);

                                sellWindow.Cancel();
                                State = ValueDumpState.WaitingToFinishQuickSell;
                                break;
                            }
                        }

                        // Update quantity sold
                        _currentItem.QuantitySold += quantity;

                        // Update station price
                        if (!_currentItem.StationBuy.HasValue)
                            _currentItem.StationBuy = price;
                        _currentItem.StationBuy = (_currentItem.StationBuy + price) / 2;

                        Logging.Log("ValueDumpUI", "Selling " + quantity + " of " + _currentItem.Name +
                            Logging.orange + " [" + Logging.white +
                            "Sell price: " + (price * quantity).ToString("#,##0.00") + 
                            Logging.orange + "]" + Logging.white + 
                            otherPrices, Logging.white);
                    }
                    sellWindow.Accept();

                    // Re-queue to check again
                    if (_currentItem.QuantitySold < _currentItem.Quantity)
                        ItemsToSell.Add(_currentItem);

                    _lastExecute = DateTime.Now;
                    State = ValueDumpState.WaitingToFinishQuickSell;
                    break;

                case ValueDumpState.WaitingToFinishQuickSell:
                    if (sellWindow == null || !sellWindow.IsReady || sellWindow.Item.ItemId != _currentItem.Id)
                    {
                        DirectWindow modal = Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.IsModal);
                        if (modal != null)
                            modal.Close();

                        State = ValueDumpState.NextItem;
                        break;
                    }
                    break;

                case ValueDumpState.RefineItems:
                    if (reprorcessingWindow == null)
                    {
                        if (DateTime.Now.Subtract(_lastExecute).TotalSeconds > (int)Time.Marketlookupdelay_seconds)
                        {
                            IEnumerable<DirectItem> refineItems = Cache.Instance.ItemHangar.Items.Where(i => ItemsToRefine.Any(r => r.Id == i.ItemId));
                            Cache.Instance.DirectEve.ReprocessStationItems(refineItems);

                            _lastExecute = DateTime.Now;
                        }
                        return;
                    }

                    if (reprorcessingWindow.NeedsQuote)
                    {
                        if (DateTime.Now.Subtract(_lastExecute).TotalSeconds > (int)Time.Marketlookupdelay_seconds)
                        {
                            reprorcessingWindow.GetQuotes();
                            _lastExecute = DateTime.Now;
                        }

                        return;
                    }

                    // Wait till we have a quote
                    if (reprorcessingWindow.Quotes.Count == 0)
                    {
                        _lastExecute = DateTime.Now;
                        return;
                    }

                    // Wait another 5 seconds to view the quote and then reprocess the stuff
                    if (DateTime.Now.Subtract(_lastExecute).TotalSeconds > (int)Time.Marketlookupdelay_seconds)
                    {
                        // TODO: We should wait for the items to appear in our hangar and then sell them...
                        reprorcessingWindow.Reprocess();
                        State = ValueDumpState.Idle;
                    }
                    break;
            }
        }

        private void btnHangar_Click(object sender, EventArgs e)
        {
            State = ValueDumpState.GetItems;
            ProcessItems();
        }

        private void ProcessItems()
        {
            // Wait for the items to load
            Logging.Log("ValueDumpUI", "Waiting for items", Logging.white);
            while (State != ValueDumpState.Idle)
            {
                System.Threading.Thread.Sleep(50);
                Application.DoEvents();
            }

            lvItems.Items.Clear();
            foreach (ItemCacheMarket item in Items.Where(i => i.InvType != null).OrderByDescending(i => i.InvType.MedianBuy * i.Quantity))
            {
                ListViewItem listItem = new ListViewItem(item.Name);
                listItem.SubItems.Add(string.Format("{0:#,##0}", item.Quantity));
                listItem.SubItems.Add(string.Format("{0:#,##0}", item.QuantitySold));
                listItem.SubItems.Add(string.Format("{0:#,##0}", item.InvType.MedianBuy));
                listItem.SubItems.Add(string.Format("{0:#,##0}", item.StationBuy));

                if (cbxSell.Checked)
                    listItem.SubItems.Add(string.Format("{0:#,##0}", item.StationBuy * item.QuantitySold));
                else
                    listItem.SubItems.Add(string.Format("{0:#,##0}", item.InvType.MedianBuy * item.Quantity));

                lvItems.Items.Add(listItem);
            }

            if (cbxSell.Checked)
            {
                tbTotalMedian.Text = string.Format("{0:#,##0}", Items.Where(i => i.InvType != null).Sum(i => i.InvType.MedianBuy * i.QuantitySold));
                tbTotalSold.Text = string.Format("{0:#,##0}", Items.Sum(i => i.StationBuy * i.QuantitySold));
            }
            else
            {
                tbTotalMedian.Text = string.Format("{0:#,##0}", Items.Where(i => i.InvType != null).Sum(i => i.InvType.MedianBuy * i.Quantity));
                tbTotalSold.Text = "";
            }
        }

        private void ValueDumpUIFormClosed(object sender, FormClosedEventArgs e)
        {
            Cache.Instance.DirectEve.Dispose();
            Cache.Instance.DirectEve = null;
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            State = ValueDumpState.Idle;
        }

        private void UpdateMineralPricesButton_Click(object sender, EventArgs e)
        {
            State = ValueDumpState.CheckMineralPrices;
        }

        private void lvItems_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            ListViewColumnSort oCompare = new ListViewColumnSort();

            if (lvItems.Sorting == SortOrder.Ascending)
                oCompare.Sorting = SortOrder.Descending;
            else
                oCompare.Sorting = SortOrder.Ascending;
            lvItems.Sorting = oCompare.Sorting;
            oCompare.ColumnIndex = e.Column;

            switch (e.Column)
            {
                case 1:
                    oCompare.CompararPor = ListViewColumnSort.TipoCompare.Cadena;
                    break;
                case 2:
                    oCompare.CompararPor = ListViewColumnSort.TipoCompare.Numero;
                    break;
                case 3:
                    oCompare.CompararPor = ListViewColumnSort.TipoCompare.Numero;
                    break;
                case 4:
                    oCompare.CompararPor = ListViewColumnSort.TipoCompare.Numero;
                    break;
                case 5:
                    oCompare.CompararPor = ListViewColumnSort.TipoCompare.Numero;
                    break;
                case 6:
                    oCompare.CompararPor = ListViewColumnSort.TipoCompare.Numero;
                    break;
            }

            lvItems.ListViewItemSorter = oCompare;
        }

        private void ValueDumpUI_Load(object sender, EventArgs e)
        {
        }

        private void lvItems_SelectedIndexChanged(object sender, EventArgs e)
        {
        }
    }
}