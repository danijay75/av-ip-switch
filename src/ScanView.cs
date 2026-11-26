using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyIpSwitch
{
    internal sealed class ScanView : CardPanel
    {
        private sealed class AdapterChoice
        {
            public string Key = "";
            public string Display = "";
            public readonly List<string> Cidrs = new List<string>();

            public override string ToString()
            {
                return Display;
            }
        }

        private const string Dash = "—";

        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _lblAdapter;
        private readonly Label _lblRange;
        private readonly Label _lblStatus;
        private readonly Label _lblDetailsTitle;
        private readonly Label _lblDetails1;
        private readonly Label _lblDetails2;
        private readonly SelectBox _combo;
        private readonly InputBox _range;
        private readonly FlatButton _btnScan;
        private readonly FlatButton _btnStop;
        private readonly CheckBox _chkAgents;
        private readonly ListView _table;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _menuCopy;
        private readonly ToolTip _tip = new ToolTip();
        private readonly System.Windows.Forms.Timer _clockTimer;

        private readonly List<AdapterChoice> _choices = new List<AdapterChoice>();
        private readonly List<ScanHost> _hosts = new List<ScanHost>();
        private readonly Dictionary<string, ListViewItem> _rows =
            new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
        private List<AdapterInfo> _adapters = new List<AdapterInfo>();

        private CancellationTokenSource _cancel;
        private bool _busy;
        private bool _suppressAdapter;
        private bool _rangeAuto = true;
        private int _progressDone;
        private int _progressTotal;
        private int _sortColumn = 1;
        private bool _sortAscending = true;
        private string _scanSummary = "";
        private int _lastProgressTick;
        private int _generation;

        public ScanView()
        {
            BackColor = Theme.Surface;

            _title = MakeLabel(Theme.CardTitle, Theme.Text);
            _title.Text = L.T("Scanner le réseau local");

            _subtitle = MakeLabel(Theme.Small, Theme.TextMuted);
            _subtitle.Text = L.T("Découvrez les machines connectées, leur adresse et leur système. Les agents ajoutent le DHCP, le pare-feu et les actions à distance.");

            _lblAdapter = MakeLabel(Theme.Small, Theme.TextMuted);
            _lblAdapter.Text = L.T("Carte réseau / sous-réseau");

            _lblRange = MakeLabel(Theme.Small, Theme.TextMuted);
            _lblRange.Text = L.T("Plage à scanner");

            _lblStatus = MakeLabel(Theme.Small, Theme.TextMuted);
            _lblStatus.Text = L.T("Prêt. Choisissez une plage puis lancez le scan.");

            _lblDetailsTitle = MakeLabel(Theme.SmallBold, Theme.TextMuted);
            _lblDetailsTitle.Text = L.T("Détails");

            _lblDetails1 = MakeLabel(Theme.Small, Theme.Text);
            _lblDetails2 = MakeLabel(Theme.Small, Theme.TextMuted);

            _combo = new SelectBox();
            _combo.SelectedIndexChanged += delegate { AdapterSelectionChanged(); };
            _combo.AccessibleName = L.T("Carte réseau / sous-réseau");

            _range = new InputBox();
            _range.Placeholder = L.T("ex. 192.168.1.0/24, 192.168.1.50-60");
            _range.AccessibleName = L.T("Plage à scanner");
            _range.ValueChanged += delegate
            {
                _rangeAuto = false;
                _range.Invalid = false;
                if (_lblStatus.ForeColor == Theme.Danger)
                {
                    SetStatus(L.T("Prêt. Choisissez une plage puis lancez le scan."), Theme.TextMuted);
                }
            };

            _btnScan = new FlatButton();
            _btnScan.Kind = ButtonKind.Primary;
            _btnScan.Text = L.T("Scanner");
            _btnScan.Click += delegate { StartScan(); };

            _btnStop = new FlatButton();
            _btnStop.Kind = ButtonKind.Secondary;
            _btnStop.Text = L.T("Arrêter");
            _btnStop.Enabled = false;
            _btnStop.Click += delegate { StopScan(); };

            _chkAgents = new CheckBox();
            _chkAgents.AutoSize = false;
            _chkAgents.FlatStyle = FlatStyle.Flat;
            _chkAgents.Font = Theme.Small;
            _chkAgents.BackColor = Theme.Surface;
            _chkAgents.ForeColor = Theme.TextMuted;
            _chkAgents.Text = L.T("Inclure les agents distants");
            _chkAgents.Checked = true;
            _chkAgents.Enabled = false;
            _tip.SetToolTip(_chkAgents, L.T("Disponible avec l'agent AV IP-Switch."));

            _table = new ListView();
            _table.View = View.Details;
            _table.FullRowSelect = true;
            _table.HideSelection = false;
            _table.MultiSelect = false;
            _table.HeaderStyle = ColumnHeaderStyle.Clickable;
            _table.BorderStyle = BorderStyle.None;
            _table.BackColor = Theme.Surface;
            _table.ForeColor = Theme.Text;
            _table.Font = Theme.Small;
            _table.HoverSelection = false;
            _table.ShowItemToolTips = true;
            _table.TabStop = true;
            _table.AccessibleName = L.T("Scan réseaux");
            _table.Columns.Add(L.T("Nom"), 120, HorizontalAlignment.Left);
            _table.Columns.Add(L.T("Adresse IP"), 110, HorizontalAlignment.Left);
            _table.Columns.Add(L.T("DHCP"), 60, HorizontalAlignment.Left);
            _table.Columns.Add(L.T("Serveur DHCP"), 110, HorizontalAlignment.Left);
            _table.Columns.Add(L.T("Pare-feu"), 80, HorizontalAlignment.Left);
            _table.Columns.Add(L.T("Agent"), 90, HorizontalAlignment.Left);
            _table.Columns.Add(L.T("Système"), 110, HorizontalAlignment.Left);
            _table.Columns.Add(L.T("Dernière réponse"), 110, HorizontalAlignment.Left);
            _table.ColumnClick += delegate(object sender, ColumnClickEventArgs e) { SortBy(e.Column); };
            _table.SelectedIndexChanged += delegate { UpdateDetails(); };
            _table.DoubleClick += delegate { CopySelectedIp(); };
            _table.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedIp();
                    e.Handled = true;
                }
            };
            _table.AccessibleDescription = L.T("Double-cliquez pour copier l'adresse IP.");
            try
            {
                typeof(ListView).GetProperty("DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_table, true, null);
            }
            catch
            {
            }

            _menu = new ContextMenuStrip();
            _menuCopy = new ToolStripMenuItem(L.T("Copier l'adresse IP"));
            _menuCopy.Click += delegate { CopySelectedIp(); };
            _menu.Items.Add(_menuCopy);
            _menu.Opening += delegate { _menuCopy.Enabled = SelectedHost() != null; };
            _table.ContextMenuStrip = _menu;

            Controls.Add(_title);
            Controls.Add(_subtitle);
            Controls.Add(_lblAdapter);
            Controls.Add(_lblRange);
            Controls.Add(_combo);
            Controls.Add(_range);
            Controls.Add(_btnScan);
            Controls.Add(_btnStop);
            Controls.Add(_chkAgents);
            Controls.Add(_lblStatus);
            Controls.Add(_table);
            Controls.Add(_lblDetailsTitle);
            Controls.Add(_lblDetails1);
            Controls.Add(_lblDetails2);

            UpdateDetails();

            _clockTimer = new System.Windows.Forms.Timer();
            _clockTimer.Interval = 5000;
            _clockTimer.Tick += delegate { UpdateTimes(); };
            _clockTimer.Start();

            LayoutView();
        }

        private static Label MakeLabel(Font font, Color color)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Theme.Surface;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            label.UseMnemonic = false;
            return label;
        }

        public void SetAdapters(List<AdapterInfo> adapters)
        {
            if (_busy)
            {
                return;
            }

            List<AdapterInfo> copy = adapters == null ? new List<AdapterInfo>() : new List<AdapterInfo>(adapters);
            _adapters = copy;

            string previous = SelectedChoiceKey();
            List<AdapterChoice> choices = new List<AdapterChoice>();

            AdapterChoice all = new AdapterChoice();
            all.Key = "";
            all.Display = L.T("Toutes les cartes");
            HashSet<string> seenCidrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < copy.Count; i++)
            {
                AdapterInfo info = copy[i];
                if (!info.EnabledKnown || !info.Enabled || info.Ip.Length == 0)
                {
                    continue;
                }

                string cidr = NetworkScanner.ToCidr(info.Ip, info.Mask);
                if (cidr.Length == 0)
                {
                    continue;
                }
                if (seenCidrs.Add(cidr))
                {
                    all.Cidrs.Add(cidr);
                }

                AdapterChoice choice = new AdapterChoice();
                choice.Key = info.Name;
                choice.Display = info.Name + "  —  " + info.Ip;
                choice.Cidrs.Add(cidr);
                choices.Add(choice);
            }

            if (all.Cidrs.Count > 0)
            {
                all.Display += " (" + all.Cidrs.Count.ToString(CultureInfo.CurrentCulture) + ")";
            }
            choices.Insert(0, all);

            _choices.Clear();
            _choices.AddRange(choices);

            _suppressAdapter = true;
            _combo.ClearItems();
            for (int i = 0; i < _choices.Count; i++)
            {
                _combo.AddItem(_choices[i]);
            }

            int index = 0;
            for (int i = 0; i < _choices.Count; i++)
            {
                if (string.Equals(_choices[i].Key, previous, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            _combo.SelectedIndex = index;
            _suppressAdapter = false;

            if (_rangeAuto)
            {
                UpdateRangeFromSelection();
            }
        }

        private string SelectedChoiceKey()
        {
            AdapterChoice choice = SelectedChoice();
            return choice == null ? "" : choice.Key;
        }

        private AdapterChoice SelectedChoice()
        {
            return _combo.SelectedItem as AdapterChoice;
        }

        private void AdapterSelectionChanged()
        {
            if (_suppressAdapter)
            {
                return;
            }
            _rangeAuto = true;
            UpdateRangeFromSelection();
        }

        private void UpdateRangeFromSelection()
        {
            AdapterChoice choice = SelectedChoice();
            _range.Value = choice == null ? "" : string.Join(", ", choice.Cidrs.ToArray());
            _range.Invalid = false;
        }

        private void StartScan()
        {
            if (_busy)
            {
                return;
            }

            List<ScanRange> ranges;
            ScanRangeParseError error;
            string token;
            if (!ScanRangeParser.TryParse(_range.Value, out ranges, out error, out token))
            {
                ShowRangeError(error, token);
                return;
            }

            long raw = 0;
            for (int i = 0; i < ranges.Count; i++)
            {
                raw += ranges[i].Count;
            }
            if (raw > ScanRangeParser.WarningThreshold)
            {
                DialogResult answer = MessageBox.Show(this,
                    L.F("Cette plage contient {0} adresses. Le scan peut prendre plusieurs minutes. Continuer ?", raw),
                    AppInfo.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            List<uint> addresses = ScanRangeParser.Expand(ranges);
            if (addresses.Count == 0)
            {
                SetStatus(L.T("Aucune adresse à scanner dans cette plage."), Theme.Danger);
                return;
            }

            _range.Invalid = false;
            _scanSummary = Shorten(_range.Value.Trim(), 48);
            _hosts.Clear();
            _rows.Clear();
            _table.Items.Clear();
            _progressDone = 0;
            _progressTotal = addresses.Count;
            _busy = true;
            SetBusy(true);
            SetStatus(L.F("Scan de {0}… {1}/{2}", _scanSummary, 0, addresses.Count), Theme.TextMuted);
            UpdateDetails();
            Invalidate();

            ScanRequest request = new ScanRequest();
            request.Addresses = addresses;
            request.Subnets = NetworkScanner.LocalSubnets(_adapters);
            request.ResolveNames = true;
            request.TimeoutMs = 500;
            request.Concurrency = 64;

            _cancel = new CancellationTokenSource();
            CancellationToken cancellationToken = _cancel.Token;
            int generation = ++_generation;

            Task task = Task.Factory.StartNew(delegate
            {
                NetworkScanner.Scan(request, cancellationToken,
                    delegate(ScanHost host) { PostHost(host, generation); },
                    delegate(int done, int total) { PostProgress(done, total, generation); });
            }, cancellationToken);

            task.ContinueWith(delegate(Task completed) { PostFinish(completed, generation); });
        }

        private static string Shorten(string text, int max)
        {
            if (text == null)
            {
                return "";
            }
            if (text.Length <= max)
            {
                return text;
            }
            return text.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        private void ShowRangeError(ScanRangeParseError error, string token)
        {
            string message;
            switch (error)
            {
                case ScanRangeParseError.Empty:
                    message = L.T("Saisissez une plage à scanner.");
                    break;
                case ScanRangeParseError.InvalidPrefix:
                    message = L.F("Préfixe invalide : {0} (0 à 32 attendu).", token);
                    break;
                case ScanRangeParseError.StartAfterEnd:
                    message = L.F("La fin de la plage précède son début : {0}.", token);
                    break;
                case ScanRangeParseError.RangeTooLarge:
                    message = L.F("Plage trop grande : {0} (maximum 65 536 adresses).", token);
                    break;
                case ScanRangeParseError.TotalTooLarge:
                    message = L.T("Trop d'adresses au total (maximum 65 536).");
                    break;
                default:
                    message = L.F("Plage invalide : {0}", token);
                    break;
            }
            _range.Invalid = true;
            SetStatus(message, Theme.Danger);
            _range.FocusText();
        }

        private void StopScan()
        {
            if (_cancel == null || _cancel.IsCancellationRequested)
            {
                return;
            }
            _cancel.Cancel();
            _btnStop.Enabled = false;
            SetStatus(L.T("Arrêt du scan…"), Theme.TextMuted);
        }

        public void Stop()
        {
            if (_cancel != null)
            {
                try
                {
                    _cancel.Cancel();
                }
                catch
                {
                }
            }
        }

        private void SetBusy(bool busy)
        {
            _btnScan.Enabled = !busy;
            _btnStop.Enabled = busy;
            _combo.Enabled = !busy;
            _range.Enabled = !busy;
        }

        private void PostHost(ScanHost host, int generation)
        {
            if (host == null || IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke(new Action<ScanHost, int>(AddHostIfCurrent), host, generation);
            }
            catch
            {
            }
        }

        private void PostProgress(int done, int total, int generation)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke(new Action<int, int, int>(UpdateProgressIfCurrent), done, total, generation);
            }
            catch
            {
            }
        }

        private void PostFinish(Task task, int generation)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke(new Action<Task, int>(FinishScanIfCurrent), task, generation);
            }
            catch
            {
            }
        }

        private void AddHostIfCurrent(ScanHost host, int generation)
        {
            if (generation == _generation)
            {
                AddHost(host);
            }
        }

        private void UpdateProgressIfCurrent(int done, int total, int generation)
        {
            if (generation == _generation)
            {
                UpdateProgress(done, total);
            }
        }

        private void FinishScanIfCurrent(Task task, int generation)
        {
            if (generation == _generation)
            {
                FinishScan(task);
            }
        }

        private void UpdateProgress(int done, int total)
        {
            if (!_busy)
            {
                return;
            }
            _progressDone = done;
            _progressTotal = total;

            int tick = Environment.TickCount;
            if (tick - _lastProgressTick >= 150 || done >= total)
            {
                _lastProgressTick = tick;
                SetStatus(L.F("Scan de {0}… {1}/{2}", _scanSummary, done, total), Theme.TextMuted);
                InvalidateProgress();
            }
        }

        private void AddHost(ScanHost host)
        {
            ListViewItem existing;
            if (_rows.TryGetValue(host.Ip, out existing))
            {
                existing.Tag = host;
                FillItem(existing, host);
                return;
            }

            _hosts.Add(host);
            ListViewItem item = CreateItem(host);
            _rows[host.Ip] = item;
            _table.Items.Add(item);
        }

        private ListViewItem CreateItem(ScanHost host)
        {
            ListViewItem item = new ListViewItem(DisplayName(host));
            item.ImageIndex = 0;
            item.Tag = host;
            item.SubItems.Add(host.Ip);
            item.SubItems.Add(Dash);
            item.SubItems.Add(Dash);
            item.SubItems.Add(Dash);
            item.SubItems.Add(Dash);
            item.SubItems.Add(FormatSystem(host.System));
            item.SubItems.Add(FormatLastSeen(host));
            item.ToolTipText = BuildTooltip(host);
            return item;
        }

        private void FillItem(ListViewItem item, ScanHost host)
        {
            item.Text = DisplayName(host);
            item.SubItems[1].Text = host.Ip;
            item.SubItems[6].Text = FormatSystem(host.System);
            item.SubItems[7].Text = FormatLastSeen(host);
            item.ToolTipText = BuildTooltip(host);
        }

        private static string DisplayName(ScanHost host)
        {
            return host.Name.Length == 0 ? Dash : host.Name;
        }

        private static string FormatSystem(HostSystem system)
        {
            switch (system)
            {
                case HostSystem.Windows: return "Windows";
                case HostSystem.MacOrLinux: return "macOS / Linux";
                case HostSystem.NetworkDevice: return L.T("Équipement réseau");
                default: return L.T("Inconnu");
            }
        }

        private static string FormatLastSeen(ScanHost host)
        {
            TimeSpan elapsed = DateTime.UtcNow - host.LastSeenUtc;
            if (elapsed.TotalSeconds < 5)
            {
                return L.T("à l'instant");
            }
            if (elapsed.TotalSeconds < 60)
            {
                return L.F("il y a {0} s", (int)elapsed.TotalSeconds);
            }
            if (elapsed.TotalMinutes < 60)
            {
                return L.F("il y a {0} min", (int)elapsed.TotalMinutes);
            }
            return L.F("il y a {0} h", (int)elapsed.TotalHours);
        }

        private static string BuildTooltip(ScanHost host)
        {
            string response = host.RttMs >= 0 ? L.F("{0} ms", host.RttMs) : L.T("Aucune réponse");
            string ttl = host.Ttl > 0 ? host.Ttl.ToString(CultureInfo.CurrentCulture) : Dash;
            return L.F("Réponse : {0}   ·   TTL : {1}   ·   Source : {2}", response, ttl, SourceLabel(host));
        }

        private static string SourceLabel(ScanHost host)
        {
            if (host.Arp && host.Icmp)
            {
                return "ARP + ICMP";
            }
            if (host.Arp)
            {
                return "ARP";
            }
            return "ICMP";
        }

        private void FinishScan(Task task)
        {
            if (!_busy)
            {
                return;
            }

            _busy = false;
            bool cancelled = (_cancel != null && _cancel.IsCancellationRequested) || task.IsCanceled;
            if (_cancel != null)
            {
                _cancel.Dispose();
                _cancel = null;
            }
            SetBusy(false);
            _progressDone = _progressTotal;
            InvalidateProgress();

            if (task.IsFaulted)
            {
                SetStatus(L.F("Le scan a échoué : {0}", ErrorText(task.Exception)), Theme.Danger);
            }
            else if (cancelled)
            {
                SetStatus(L.F("Scan arrêté : {0} machine(s) trouvée(s).", _hosts.Count), Theme.Warning);
            }
            else if (_hosts.Count == 0)
            {
                SetStatus(L.T("Aucune machine trouvée. Vérifiez la plage et le pare-feu."), Theme.Warning);
            }
            else
            {
                SetStatus(L.F("Scan terminé : {0} machine(s) trouvée(s).", _hosts.Count), Theme.Success);
            }

            SortHosts();
            RefreshTable();
            UpdateDetails();
        }

        private static string ErrorText(Exception exception)
        {
            Exception current = exception;
            while (current != null && current.InnerException != null)
            {
                current = current.InnerException;
            }
            return current == null ? L.T("Erreur inconnue.") : current.Message;
        }

        private void SortBy(int column)
        {
            if (column < 0 || column >= _table.Columns.Count)
            {
                return;
            }
            if (_sortColumn == column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = column;
                _sortAscending = true;
            }
            SortHosts();
            RefreshTable();
        }

        private void SortHosts()
        {
            int column = _sortColumn;
            bool ascending = _sortAscending;
            _hosts.Sort(delegate(ScanHost a, ScanHost b)
            {
                int result = CompareHosts(a, b, column);
                return ascending ? result : -result;
            });
        }

        private static int CompareHosts(ScanHost a, ScanHost b, int column)
        {
            switch (column)
            {
                case 1:
                    return a.Address.CompareTo(b.Address);
                case 2:
                    return string.Compare(Dash, Dash, StringComparison.Ordinal);
                case 3:
                    return string.Compare(Dash, Dash, StringComparison.Ordinal);
                case 4:
                    return string.Compare(Dash, Dash, StringComparison.Ordinal);
                case 5:
                    return string.Compare(Dash, Dash, StringComparison.Ordinal);
                case 6:
                    return string.Compare(FormatSystem(a.System), FormatSystem(b.System),
                        StringComparison.CurrentCultureIgnoreCase);
                case 7:
                    return a.LastSeenUtc.CompareTo(b.LastSeenUtc);
                default:
                    return string.Compare(DisplayName(a), DisplayName(b),
                        StringComparison.CurrentCultureIgnoreCase);
            }
        }

        private void RefreshTable()
        {
            _table.BeginUpdate();
            try
            {
                _table.Items.Clear();
                _rows.Clear();
                for (int i = 0; i < _hosts.Count; i++)
                {
                    ListViewItem item = CreateItem(_hosts[i]);
                    _rows[_hosts[i].Ip] = item;
                    _table.Items.Add(item);
                }
            }
            finally
            {
                _table.EndUpdate();
            }
        }

        private void UpdateTimes()
        {
            if (_hosts.Count == 0 || _table.Items.Count == 0)
            {
                return;
            }
            _table.BeginUpdate();
            try
            {
                for (int i = 0; i < _hosts.Count; i++)
                {
                    ListViewItem item;
                    if (_rows.TryGetValue(_hosts[i].Ip, out item) && item.SubItems.Count > 7)
                    {
                        item.SubItems[7].Text = FormatLastSeen(_hosts[i]);
                    }
                }
            }
            finally
            {
                _table.EndUpdate();
            }
        }

        private ScanHost SelectedHost()
        {
            if (_table.SelectedItems.Count == 0)
            {
                return null;
            }
            return _table.SelectedItems[0].Tag as ScanHost;
        }

        private void UpdateDetails()
        {
            ScanHost host = SelectedHost();
            if (host == null)
            {
                _lblDetails1.Text = L.T("Sélectionnez une machine pour voir ses détails.");
                _lblDetails2.Text = "";
                return;
            }

            string name = host.Name.Length > 0 ? host.Name : Dash;
            string mac = host.Mac.Length > 0 ? host.Mac : Dash;
            _lblDetails1.Text = L.F("Nom : {0}   ·   MAC : {1}   ·   Système : {2}",
                name, mac, FormatSystem(host.System));

            string response = host.RttMs >= 0 ? L.F("{0} ms", host.RttMs) : Dash;
            string ttl = host.Ttl > 0 ? host.Ttl.ToString(CultureInfo.CurrentCulture) : Dash;
            _lblDetails2.Text = L.F("Réponse : {0}   ·   TTL : {1}   ·   Source : {2}",
                response, ttl, SourceLabel(host));
        }

        private void CopySelectedIp()
        {
            ScanHost host = SelectedHost();
            if (host == null)
            {
                return;
            }
            try
            {
                Clipboard.SetText(host.Ip);
                SetStatus(L.T("Adresse IP copiée."), Theme.Success);
            }
            catch (Exception ex)
            {
                SetStatus(L.F("Impossible de copier l'adresse IP : {0}", ex.Message), Theme.Danger);
            }
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
            _lblStatus.BackColor = Theme.Surface;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && !_busy && _range.ContainsFocus)
            {
                StartScan();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutView();
        }

        private void LayoutView()
        {
            if (_title == null || _table == null)
            {
                return;
            }

            int pad = Theme.S(24);
            int inner = Math.Max(Theme.S(200), Width - pad * 2);

            _title.SetBounds(pad, Theme.S(18), inner, Theme.S(26));
            _subtitle.SetBounds(pad, Theme.S(46), inner, Theme.S(20));

            int labelTop = Theme.S(80);
            int fieldTop = Theme.S(100);
            int fieldHeight = Theme.S(36);
            int gap = Theme.S(12);

            int btnScanWidth = Theme.S(96);
            int btnStopWidth = Theme.S(88);
            int buttons = btnScanWidth + btnStopWidth + gap * 3;
            int comboWidth = Math.Max(Theme.S(170), (int)(inner * 0.27f));
            int rangeWidth = inner - comboWidth - buttons;
            if (rangeWidth < Theme.S(150))
            {
                comboWidth = Math.Max(Theme.S(150), inner - buttons - Theme.S(150));
                rangeWidth = Math.Max(Theme.S(120), inner - comboWidth - buttons);
            }

            _lblAdapter.SetBounds(pad, labelTop, comboWidth, Theme.S(18));
            _lblRange.SetBounds(pad + comboWidth + gap, labelTop, rangeWidth, Theme.S(18));

            _combo.SetBounds(pad, fieldTop, comboWidth, Theme.S(34));
            _range.SetBounds(pad + comboWidth + gap, fieldTop, rangeWidth, fieldHeight);
            int buttonLeft = pad + comboWidth + gap + rangeWidth + gap;
            _btnScan.SetBounds(buttonLeft, fieldTop, btnScanWidth, fieldHeight);
            _btnStop.SetBounds(buttonLeft + btnScanWidth + gap, fieldTop, btnStopWidth, fieldHeight);

            _chkAgents.SetBounds(pad, Theme.S(142), Theme.S(300), Theme.S(24));
            int statusLeft = pad + Theme.S(310);
            _lblStatus.SetBounds(statusLeft, Theme.S(142), Math.Max(Theme.S(120), Width - pad - statusLeft), Theme.S(24));

            int tableTop = Theme.S(182);
            int detailsHeight = Theme.S(84);
            int detailsTop = Math.Max(tableTop + Theme.S(80), Height - detailsHeight);
            _table.SetBounds(pad, tableTop, inner, Math.Max(Theme.S(60), detailsTop - tableTop - Theme.S(10)));

            _lblDetailsTitle.SetBounds(pad, detailsTop + Theme.S(8), inner, Theme.S(18));
            _lblDetails1.SetBounds(pad, detailsTop + Theme.S(28), inner, Theme.S(18));
            _lblDetails2.SetBounds(pad, detailsTop + Theme.S(48), inner, Theme.S(18));

            LayoutColumns();
            InvalidateProgress();
        }

        private void LayoutColumns()
        {
            int available = _table.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Theme.S(2);
            if (available < Theme.S(320))
            {
                available = Math.Max(Theme.S(320), Width - Theme.S(48));
            }

            int[] weights = new int[] { 22, 13, 8, 13, 9, 10, 12, 13 };
            int used = 0;
            for (int i = 0; i < _table.Columns.Count - 1; i++)
            {
                int width = Math.Max(Theme.S(50), available * weights[i] / 100);
                _table.Columns[i].Width = width;
                used += width;
            }
            _table.Columns[_table.Columns.Count - 1].Width = Math.Max(Theme.S(60), available - used);
        }

        private void InvalidateProgress()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }
            Invalidate(new Rectangle(0, Theme.S(168), Width, Theme.S(12)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            int pad = Theme.S(24);
            int inner = Math.Max(0, Width - pad * 2);
            int barTop = Theme.S(172);
            int barHeight = Theme.S(4);

            using (SolidBrush track = new SolidBrush(Theme.SurfaceSunken))
            {
                e.Graphics.FillRectangle(track, pad, barTop, inner, barHeight);
            }
            if (_progressTotal > 0 && _progressDone > 0)
            {
                int width = (int)((long)inner * _progressDone / _progressTotal);
                if (width > 0)
                {
                    using (SolidBrush fill = new SolidBrush(Theme.Accent))
                    {
                        e.Graphics.FillRectangle(fill, pad, barTop, Math.Min(inner, width), barHeight);
                    }
                }
            }

            int detailsTop = Math.Max(Theme.S(182) + Theme.S(80), Height - Theme.S(84));
            using (Pen pen = new Pen(Theme.Border, 1f))
            {
                e.Graphics.DrawLine(pen, pad, detailsTop, Width - pad, detailsTop);
            }
        }

        public void ApplyLanguage()
        {
            _title.Text = L.T("Scanner le réseau local");
            _subtitle.Text = L.T("Découvrez les machines connectées, leur adresse et leur système. Les agents ajoutent le DHCP, le pare-feu et les actions à distance.");
            _lblAdapter.Text = L.T("Carte réseau / sous-réseau");
            _lblRange.Text = L.T("Plage à scanner");
            _lblDetailsTitle.Text = L.T("Détails");
            _range.Placeholder = L.T("ex. 192.168.1.0/24, 192.168.1.50-60");
            _btnScan.Text = L.T("Scanner");
            _btnStop.Text = L.T("Arrêter");
            _chkAgents.Text = L.T("Inclure les agents distants");
            _menuCopy.Text = L.T("Copier l'adresse IP");
            _combo.AccessibleName = L.T("Carte réseau / sous-réseau");
            _range.AccessibleName = L.T("Plage à scanner");
            _table.AccessibleName = L.T("Scan réseaux");
            _table.AccessibleDescription = L.T("Double-cliquez pour copier l'adresse IP.");
            _tip.SetToolTip(_chkAgents, L.T("Disponible avec l'agent AV IP-Switch."));

            _table.Columns[0].Text = L.T("Nom");
            _table.Columns[1].Text = L.T("Adresse IP");
            _table.Columns[2].Text = L.T("DHCP");
            _table.Columns[3].Text = L.T("Serveur DHCP");
            _table.Columns[4].Text = L.T("Pare-feu");
            _table.Columns[5].Text = L.T("Agent");
            _table.Columns[6].Text = L.T("Système");
            _table.Columns[7].Text = L.T("Dernière réponse");

            if (_choices.Count > 0)
            {
                string previous = SelectedChoiceKey();
                SetAdapters(_adapters);
                for (int i = 0; i < _choices.Count; i++)
                {
                    if (string.Equals(_choices[i].Key, previous, StringComparison.OrdinalIgnoreCase))
                    {
                        _suppressAdapter = true;
                        _combo.SelectedIndex = i;
                        _suppressAdapter = false;
                        break;
                    }
                }
            }

            if (!_busy)
            {
                SetStatus(L.T("Prêt. Choisissez une plage puis lancez le scan."), Theme.TextMuted);
            }
            RefreshTable();
            UpdateDetails();
            LayoutView();
            Invalidate();
        }

        public override void ApplyTheme()
        {
            base.ApplyTheme();

            Label[] labels = new Label[]
            {
                _title, _subtitle, _lblAdapter, _lblRange, _lblStatus,
                _lblDetailsTitle, _lblDetails1, _lblDetails2
            };
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].BackColor = Theme.Surface;
            }
            _title.ForeColor = Theme.Text;
            _subtitle.ForeColor = Theme.TextMuted;
            _lblAdapter.ForeColor = Theme.TextMuted;
            _lblRange.ForeColor = Theme.TextMuted;
            _lblDetailsTitle.ForeColor = Theme.TextMuted;
            _lblDetails1.ForeColor = Theme.Text;
            _lblDetails2.ForeColor = Theme.TextMuted;

            _chkAgents.BackColor = Theme.Surface;
            _chkAgents.ForeColor = Theme.TextMuted;
            _table.BackColor = Theme.Surface;
            _table.ForeColor = Theme.Text;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
                if (_cancel != null)
                {
                    _cancel.Dispose();
                    _cancel = null;
                }
                if (_clockTimer != null)
                {
                    _clockTimer.Stop();
                    _clockTimer.Dispose();
                }
                if (_menu != null)
                {
                    _menu.Dispose();
                }
                if (_tip != null)
                {
                    _tip.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
