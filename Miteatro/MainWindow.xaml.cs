using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
namespace Miteatro
{
    public partial class MainWindow : Window
    {
        TheaterService service; ConnectionSettings connection; readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiTeatro");
        List<SaleLine> cart = new List<SaleLine>(); string requestId = Guid.NewGuid().ToString(); ComboBox shows, products, fares, payment; TextBox quantity, productQuantity, coupon, cash, reference; TextBlock availability, total, turnSummary; DataGrid cartTable, history, shiftTable; DatePicker from, to; ComboBox reportKind; DataGrid reportTable; List<string[]> reportRows = new List<string[]>();
        DateTime lastInput = DateTime.Now; DispatcherTimer timer; bool authenticating; bool rebuilding;
        public MainWindow()
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("es-MX");
            InitializeComponent();
            PreviewMouseDown += (s, e) => lastInput = DateTime.Now;
            PreviewKeyDown += (s, e) => lastInput = DateTime.Now;
            System.Windows.Input.InputManager.Current.PreProcessInput += TrackInput;
            Loaded += (s, e) => Start();
        }
        void TrackInput(object sender, System.Windows.Input.PreProcessInputEventArgs e)
        {
            if (e.StagingItem.Input is System.Windows.Input.KeyEventArgs || e.StagingItem.Input is System.Windows.Input.MouseButtonEventArgs || e.StagingItem.Input is System.Windows.Input.TextCompositionEventArgs)
                lastInput = DateTime.Now;
        }
        string ConfigPath => Path.Combine(folder, "connection.xml");
        void Start()
        {
            try
            {
                connection = File.Exists(ConfigPath) ? Codec.Decode<ConnectionSettings>(File.ReadAllText(ConfigPath)) : new ConnectionSettings();
                Connect();
                Authenticate();
                timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                timer.Tick += (s, e) => { if (service?.Current != null && !authenticating && DateTime.Now - lastInput > TimeSpan.FromMinutes(service.Data.Settings.TimeoutMinutes)) { foreach (Window w in OwnedWindows.Cast<Window>().ToList()) w.Close(); service.Logout(); Authenticate(); } service?.AutomaticBackup(); };
                timer.Start();
                SystemEvents.SessionSwitch += SessionSwitch;
            }
            catch (Exception ex) { MessageBox.Show("No se pudo abrir el sistema. Los datos se conservaron.\n" + ex.Message, "Mi Teatro"); Close(); }
        }
        void SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
                Dispatcher.BeginInvoke(new Action(() => { foreach (Window w in OwnedWindows.Cast<Window>().ToList()) w.Close(); service?.Logout(); if (!authenticating) Authenticate(); }));
        }
        void Connect()
        {
            service = new TheaterService(connection.MySql ? (IRepository)new MySqlRepository(connection.ConnectionString()) : new LocalRepository(Path.Combine(folder, "theater.xml")));
        }
        void Authenticate()
        {
            authenticating = true;
            Tabs.IsEnabled = false;
            Tabs.Visibility = Visibility.Collapsed;
            AccountPanel.Children.Clear();
            try
            {
                if (service.Data.Users.Count == 0)
                {
                    var setup = new FormDialog(this, "Configura el administrador");
                    setup.Text("name", "Nombre completo");
                    setup.Text("login", "Usuario");
                    setup.Secret("password", "Contraseña (mínimo 10 caracteres)");
                    setup.Secret("confirm", "Repetir contraseña");
                    setup.Submit = () => { TheaterService.Require(setup.Get("password") == setup.Get("confirm"), "Las contraseñas no coinciden."); service.Setup(setup.Get("name"), setup.Get("login"), setup.Get("password")); };
                    if (setup.ShowDialog() != true)
                    {
                        Close();
                        return;
                    }
                }
                var form = new FormDialog(this, "Iniciar sesión");
                form.Text("login", "Usuario");
                form.Secret("password", "Contraseña");
                form.Submit = () => service.Login(form.Get("login"), form.Get("password"));
                if (form.ShowDialog() != true)
                {
                    Close();
                    return;
                }
                lastInput = DateTime.Now;
                Tabs.IsEnabled = true;
                Tabs.Visibility = Visibility.Visible;
                Build();
            }
            finally { authenticating = false; }
        }
        void Run(Action action)
        {
            try
            {
                action();
                if (service.BackupError != null)
                    Status.Text = service.BackupError;
            }
            catch (Exception ex) { Status.Text = ex.Message; MessageBox.Show(this, ex.Message, "Mi Teatro", MessageBoxButton.OK, MessageBoxImage.Information); try { Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "errors.log"), DateTime.Now + " " + ex.GetType().Name + " " + ex.Message + Environment.NewLine); } catch { } }
        }
        void Build()
        {
            rebuilding = true;
            int selected = Tabs.SelectedIndex;
            Tabs.Items.Clear();
            AccountPanel.Children.Clear();
            AccountPanel.Children.Add(Ui.Text(service.Current.Name + " · " + service.Current.Role + "  "));
            AccountPanel.Children.Add(Ui.Button("Contraseña", () => Run(ChangePassword)));
            AccountPanel.Children.Add(Ui.Button("Bloquear", () => { service.Logout(); Authenticate(); }));
            BuildSales();
            BuildTill();
            BuildHistory();
            BuildReports();
            if (service.Manager)
            {
                BuildCatalog();
                BuildStock();
            }
            if (service.Admin)
            {
                BuildUsers();
                BuildPromotions();
                BuildSettings();
            }
            if (service.Manager)
                BuildAudit();
            Tabs.SelectedIndex = selected >= 0 && selected < Tabs.Items.Count ? selected : 0;
            rebuilding = false;
            RefreshSale();
            Status.Text = service.Storage + " · " + service.Current.Name + " · " + (service.Active == null ? "Caja cerrada" : "Turno de " + service.Active.Cashier);
        }
        void AddTab(string title, UIElement content)
        {
            Tabs.Items.Add(new TabItem { Header = title, Content = content });
        }
        StackPanel Page(string title)
        {
            var p = new StackPanel { Margin = new Thickness(20) };
            p.Children.Add(Ui.Text(title, 24));
            return p;
        }
        void AddPage(string title, StackPanel page)
        {
            AddTab(title, new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        void Field(Panel panel, string label, Control input)
        {
            panel.Children.Add(Ui.Text(label));
            panel.Children.Add(input);
        }
        TextBox Box(string value = "") => new TextBox { Text = value };
        ComboBox Combo(IEnumerable<string> values)
        {
            var c = new ComboBox { ItemsSource = values.ToList() };
            c.SelectedIndex = 0;
            return c;
        }
        T Selected<T>(DataGrid grid)
        {
            TheaterService.Require(grid.SelectedItem is T, "Selecciona un registro.");
            return (T)grid.SelectedItem;
        }
        string Approval()
        {
            if (service.Manager)
                return service.Current.Id;
            string id = null;
            var f = new FormDialog(this, "Autorización de supervisor");
            f.Text("login", "Usuario supervisor o administrador");
            f.Secret("password", "Contraseña");
            f.Submit = () => id = service.Authorize(f.Get("login"), f.Get("password"));
            TheaterService.Require(f.ShowDialog() == true, "Operación sin autorizar.");
            return id;
        }
        bool Confirm(string text) => MessageBox.Show(this, text, "Mi Teatro", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        decimal Parse(TextBox box)
        {
            decimal n;
            TheaterService.Require(decimal.TryParse(box.Text, out n), "Ingresa un monto válido.");
            TheaterService.Money(n);
            return n;
        }
        int Count(TextBox box)
        {
            int n;
            TheaterService.Require(int.TryParse(box.Text, out n) && n > 0 && n <= 10000, "Ingresa una cantidad entre 1 y 10000.");
            return n;
        }
        void BuildSales()
        {
            var grid = new Grid { Margin = new Thickness(20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel();
            left.Children.Add(Ui.Text("Una nueva visita al teatro", 24));
            shows = new ComboBox { ItemsSource = service.Data.Performances.Where(p => !p.Cancelled && p.Starts > DateTime.Now).OrderBy(p => p.Starts).ToList() };
            shows.SelectedIndex = 0;
            shows.SelectionChanged += (s, e) => { if (!rebuilding) RefreshSale(); };
            Field(left, "Función", shows);
            availability = Ui.Text("");
            left.Children.Add(availability);
            fares = Combo(new[] { "Adulto", "Niño", "Adulto mayor" });
            Field(left, "Tipo de boleto", fares);
            quantity = Box("1");
            Field(left, "Cantidad de entradas", quantity);
            left.Children.Add(Ui.Button("Agregar entradas", () => Run(() => { var p = shows.SelectedItem as Performance; TheaterService.Require(p != null, "Crea o selecciona una función."); var line = service.TicketLine(p.Id, (string)fares.SelectedItem, Count(quantity)); TheaterService.Require(cart.Where(l => l.Ticket && l.ItemId == p.Id).Sum(l => l.Quantity) + line.Quantity <= service.Available(p.Id), "Cupo insuficiente."); cart.Add(line); RefreshSale(); })));
            left.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
            left.Children.Add(Ui.Text("Dulcería", 21));
            products = new ComboBox { ItemsSource = service.Data.Products.Where(p => p.Enabled).ToList() };
            products.SelectedIndex = 0;
            Field(left, "Producto", products);
            productQuantity = Box("1");
            Field(left, "Cantidad de productos", productQuantity);
            left.Children.Add(Ui.Button("Agregar producto", () => Run(() => { var p = products.SelectedItem as Product; TheaterService.Require(p != null, "Crea o selecciona un producto."); var line = service.ProductLine(p.Id, Count(productQuantity)); TheaterService.Require(cart.Where(l => !l.Ticket && l.ItemId == p.Id).Sum(l => l.Quantity) + line.Quantity <= p.Stock, "Existencias insuficientes."); cart.Add(line); RefreshSale(); })));
            grid.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var right = new StackPanel();
            right.Children.Add(Ui.Text("Tu venta", 24));
            cartTable = Ui.Table();
            cartTable.Height = 140;
            cartTable.AutoGenerateColumns = false;
            foreach (var pair in new[] { new[] { "Concepto", "Name" }, new[] { "Tarifa", "Fare" }, new[] { "Cantidad", "Quantity" }, new[] { "Precio", "UnitPrice" } })
                cartTable.Columns.Add(new DataGridTextColumn { Header = pair[0], Binding = new System.Windows.Data.Binding(pair[1]), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            right.Children.Add(cartTable);
            right.Children.Add(Ui.Row(Ui.Button("Quitar", () => Run(() => { cart.Remove(Selected<SaleLine>(cartTable)); RefreshSale(); })), Ui.Button("Vaciar", () => { if (Confirm("¿Vaciar el carrito pendiente?")) { cart.Clear(); requestId = Guid.NewGuid().ToString(); RefreshSale(); } })));
            coupon = Box();
            Field(right, "Código de promoción / cupón", coupon);
            right.Children.Add(Ui.Button("Calcular total", () => Run(RefreshQuote)));
            total = Ui.Text("Total $0.00", 26);
            right.Children.Add(total);
            payment = Combo(new[] { "Efectivo", "Tarjeta", "Mixto" });
            Field(right, "Método de pago", payment);
            cash = Box("0");
            Field(right, "Efectivo recibido (en mixto, parte en efectivo)", cash);
            reference = Box();
            var referenceLabel = Ui.Text("Referencia del pago con tarjeta aprobado");
            right.Children.Add(referenceLabel);
            right.Children.Add(reference);
            reference.Visibility = referenceLabel.Visibility = Visibility.Collapsed;
            payment.SelectionChanged += (s, e) => { reference.Visibility = referenceLabel.Visibility = payment.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible; cash.IsEnabled = payment.SelectedIndex != 1; };
            right.Children.Add(Ui.Button("Cobrar venta", () => Run(Checkout)));
            var scroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(scroll, 2);
            grid.Children.Add(scroll);
            AddTab("Venta", grid);
        }
        void RefreshSale()
        {
            if (cartTable == null)
                return;
            cartTable.ItemsSource = null;
            cartTable.ItemsSource = cart;
            var p = shows.SelectedItem as Performance;
            availability.Text = p == null ? "Sin funciones disponibles" : (service.Available(p.Id) - cart.Where(l => l.Ticket && l.ItemId == p.Id).Sum(l => l.Quantity)) + " lugares disponibles · Adulto " + p.Adult.ToString("C") + " · Niño " + p.Child.ToString("C") + " · Mayor " + p.Senior.ToString("C");
            total.Text = "Subtotal " + cart.Sum(l => l.Gross).ToString("C");
        }
        void RefreshQuote()
        {
            var quote = service.Quote(cart, coupon.Text);
            total.Text = "Total " + quote.Sum(l => l.Total).ToString("C") + " · Descuento " + quote.Sum(l => l.Discount).ToString("C");
        }
        void Checkout()
        {
            var quote = service.Quote(cart, coupon.Text);
            decimal amount = quote.Sum(l => l.Total), discount = quote.Sum(l => l.Discount);
            string method = (string)payment.SelectedItem;
            decimal received = method == "Tarjeta" ? 0 : Parse(cash);
            decimal change = method == "Efectivo" ? received - amount : 0;
            TheaterService.Require(method != "Efectivo" || change >= 0, "Efectivo insuficiente.");
            if (!Confirm("Total: " + amount.ToString("C") + "\nDescuento: " + discount.ToString("C") + "\nMétodo: " + method + "\nCambio: " + change.ToString("C") + "\n\n¿Confirmas que el pago fue recibido?"))
                return;
            string approver = discount > service.Data.Settings.DiscountApproval ? Approval() : null;
            var sale = service.Checkout(cart, coupon.Text, method, received, reference.Text, approver, requestId);
            cart.Clear();
            requestId = Guid.NewGuid().ToString();
            Build();
            MessageBox.Show(this, "Venta " + sale.Folio + " guardada.\nCambio: " + change.ToString("C") + "\nPuedes imprimir el comprobante y los boletos desde Ventas.", "Venta registrada");
        }
        void BuildTill()
        {
            var page = Page("Caja 01 · Control del turno");
            turnSummary = Ui.Text(service.Active == null ? "Caja cerrada" : "Turno de " + service.Active.Cashier + " · Abierto " + service.Active.Opened.ToString("g") + "\nEfectivo esperado: " + service.Expected(service.Active.Id).ToString("C"), 18);
            page.Children.Add(turnSummary);
            page.Children.Add(Ui.Row(Ui.Button("Abrir caja", () => Run(() => { var f = new FormDialog(this, "Abrir turno"); f.Text("cash", "Fondo inicial", "0"); f.Submit = () => service.OpenShift(f.Number("cash")); if (f.ShowDialog() == true) Build(); })), Ui.Button("Entrada / retiro", () => Run(() => { var f = new FormDialog(this, "Movimiento de efectivo"); f.Choice("kind", "Tipo", new[] { "Entrada", "Retiro" }); f.Text("amount", "Monto"); f.Text("reason", "Motivo"); f.Submit = () => { var n = f.Number("amount"); TheaterService.Money(n); service.MoveCash(f.Get("kind") == "Retiro" ? -n : n, f.Get("reason"), Approval()); }; if (f.ShowDialog() == true) Build(); })), Ui.Button("Realizar corte", () => Run(() => { TheaterService.Require(cart.Count == 0, "Completa o vacía el carrito antes del corte."); TheaterService.Require(service.Active != null, "No hay turno abierto."); var f = new FormDialog(this, "Corte de caja"); f.Text("counted", "Efectivo contado"); f.Submit = () => { decimal n = f.Number("counted"); TheaterService.Require(Confirm("Esperado: " + service.Expected(service.Active.Id).ToString("C") + "\nContado: " + n.ToString("C") + "\nDiferencia: " + (n - service.Expected(service.Active.Id)).ToString("C") + "\n¿Cerrar turno?"), "Corte no confirmado."); service.CloseShift(n); }; if (f.ShowDialog() == true) Build(); }))));
            page.Children.Add(Ui.Text("Cortes y turnos", 20));
            shiftTable = Ui.Table();
            shiftTable.Height = 240;
            shiftTable.ItemsSource = service.Data.Shifts.Where(s => service.Manager || s.UserId == service.Current.Id).OrderByDescending(s => s.Opened).Select(s => new { Id = s.Id, Cajero = s.Cashier, Apertura = s.Opened, Cierre = s.Closed, Fondo = s.Opening, Esperado = s.Closed.HasValue ? s.Expected : service.Expected(s.Id), Contado = s.Counted, Diferencia = s.Closed.HasValue ? (decimal?)s.Difference : null }).ToList();
            page.Children.Add(shiftTable);
            page.Children.Add(Ui.Button("Imprimir corte seleccionado", () => Run(() => { TheaterService.Require(shiftTable.SelectedItem != null, "Selecciona un turno."); string id = (string)shiftTable.SelectedItem.GetType().GetProperty("Id").GetValue(shiftTable.SelectedItem); var shift = service.Data.Shifts.Single(s => s.Id == id); PrintText("Corte de caja", new[] { service.Data.Settings.Theater, shift.ToString(), "Fondo: " + shift.Opening.ToString("C"), "Esperado: " + (shift.Closed.HasValue ? shift.Expected : service.Expected(id)).ToString("C"), "Contado: " + shift.Counted.ToString("C"), "Diferencia: " + shift.Difference.ToString("C") }); })));
            page.Children.Add(Ui.Text("Movimientos de efectivo", 20));
            var moves = Ui.Table();
            moves.Height = 180;
            moves.ItemsSource = service.Data.CashMovements.Where(m => service.Manager || service.Data.Shifts.Any(s => s.Id == m.ShiftId && s.UserId == service.Current.Id)).OrderByDescending(m => m.Date).Select(m => new { Fecha = m.Date, Monto = m.Amount, Motivo = m.Reason }).ToList();
            page.Children.Add(moves);
            AddPage("Caja", page);
        }
        void BuildHistory()
        {
            var page = Page("Ventas, boletos y devoluciones");
            history = Ui.Table();
            history.Height = 300;
            history.ItemsSource = service.Data.Sales.OrderByDescending(s => s.Date).ToList();
            history.AutoGenerateColumns = false;
            foreach (var field in new[] { "Folio", "Date", "Cashier", "Total", "Cash", "Card", "Status" })
                history.Columns.Add(new DataGridTextColumn { Header = new Dictionary<string, string> { { "Folio", "Folio" }, { "Date", "Fecha" }, { "Cashier", "Cajero" }, { "Total", "Total" }, { "Cash", "Efectivo" }, { "Card", "Tarjeta" }, { "Status", "Estado" } }[field], Binding = new System.Windows.Data.Binding(field) { StringFormat = field == "Date" ? "dd/MM/yyyy HH:mm" : new[] { "Total", "Cash", "Card" }.Contains(field) ? "C2" : null }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            page.Children.Add(history);
            page.Children.Add(Ui.Row(Ui.Button("Ver detalle", () => Run(() => ShowSale(Selected<Sale>(history)))), Ui.Button("Comprobante", () => Run(() => PrintSale(Selected<Sale>(history), false))), Ui.Button("Boletos individuales", () => Run(() => PrintSale(Selected<Sale>(history), true)))));
            page.Children.Add(Ui.Row(Ui.Button("Devolución parcial", () => Run(() => ReturnSale(false))), Ui.Button("Cancelar venta completa", () => Run(() => ReturnSale(true)))));
            page.Children.Add(Ui.Text("Validar entrada", 20));
            var code = Box();
            Field(page, "Folio completo del boleto", code);
            page.Children.Add(Ui.Button("Registrar entrada", () => Run(() => { service.UseTicket(code.Text); code.Clear(); MessageBox.Show("Entrada registrada.", "Mi Teatro"); })));
            AddPage("Ventas", page);
        }
        void ShowSale(Sale sale)
        {
            var text = sale + "\n\n" + string.Join("\n", sale.Lines) + "\n\nPromoción: " + sale.Promotion + "\nReferencia: " + sale.Reference + "\n\nReembolsos:\n" + string.Join("\n", service.Data.Refunds.Where(r => r.SaleId == sale.Id).Select(r => r.Date.ToString("g") + " · " + r.Amount.ToString("C") + " · " + r.Reason));
            MessageBox.Show(this, text, "Detalle de venta");
        }
        void ReturnSale(bool full)
        {
            var sale = Selected<Sale>(history);
            var lines = sale.Lines.Where(l => l.Returned < l.Quantity).ToList();
            TheaterService.Require(lines.Count > 0, "La venta ya fue devuelta por completo.");
            var f = new FormDialog(this, full ? "Cancelar venta completa" : "Devolución parcial");
            if (!full)
            {
                f.Choice("line", "Artículo", lines.Select(l => l.Id + " | " + l.ToString()));
                f.Text("quantity", "Cantidad", "1");
            }
            f.Check("restock", "Reintegrar productos devueltos al inventario", true);
            f.Text("reason", "Motivo obligatorio");
            f.Text("reference", "Referencia del reembolso en terminal (si hubo tarjeta)");
            f.Submit = () => { string id = full ? null : f.Get("line").Split('|')[0].Trim(); int q = full ? 0 : f.Integer("quantity"); decimal amount = full ? lines.Sum(l => service.RefundAmount(l, l.Quantity - l.Returned)) : service.RefundAmount(lines.Single(l => l.Id == id), q); TheaterService.Require(Confirm("Se registrará un reembolso de " + amount.ToString("C") + ".\nEl reembolso de tarjeta debe realizarse en la terminal.\n¿Continuar?"), "Devolución no confirmada."); service.Return(sale.Id, id, q, full, f.Bool("restock"), f.Get("reason"), f.Get("reference"), Approval()); };
            if (f.ShowDialog() == true)
                Build();
        }
        void PrintText(string title, IEnumerable<string> content)
        {
            var dialog = new PrintDialog();
            if (!string.IsNullOrWhiteSpace(service.Data.Settings.Printer))
            {
                try
                {
                    dialog.PrintQueue = new System.Printing.LocalPrintServer().GetPrintQueue(service.Data.Settings.Printer);
                }
                catch { }
            }
            if (dialog.ShowDialog() != true)
                return;
            var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 12, PageWidth = dialog.PrintableAreaWidth, PageHeight = dialog.PrintableAreaHeight, PagePadding = new Thickness(24) };
            foreach (var line in content)
                doc.Blocks.Add(new Paragraph(new Run(line)));
            dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, title);
        }
        void PrintSale(Sale sale, bool tickets)
        {
            var dialog = new PrintDialog();
            if (!string.IsNullOrWhiteSpace(service.Data.Settings.Printer))
            {
                try
                {
                    dialog.PrintQueue = new System.Printing.LocalPrintServer().GetPrintQueue(service.Data.Settings.Printer);
                }
                catch { }
            }
            if (dialog.ShowDialog() != true)
                return;
            service.PrintRecorded(sale.Id, tickets);
            var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 12, PageWidth = dialog.PrintableAreaWidth, PageHeight = dialog.PrintableAreaHeight, PagePadding = new Thickness(24) };
            string copy = (tickets ? sale.TicketPrints : sale.Prints) > 1 ? "REIMPRESIÓN" : "ORIGINAL";
            if (tickets)
            {
                var valid = service.Data.Tickets.Where(t => t.SaleId == sale.Id && !t.Void).ToList();
                TheaterService.Require(valid.Count > 0, "La venta no tiene boletos vigentes.");
                foreach (var t in valid)
                {
                    var p = service.Data.Performances.Single(x => x.Id == t.PerformanceId);
                    var l = sale.Lines.Single(x => x.Id == t.LineId);
                    var section = new Section { BreakPageBefore = doc.Blocks.Count > 0 };
                    section.Blocks.Add(new Paragraph(new Run(service.Data.Settings.Theater + "\nBOLETO · " + copy + "\n" + p.Name + "\n" + p.Starts.ToString("f") + "\nSala: " + p.Room + "\nEntrada general · " + l.Fare + "\n" + t.Code + "\nVenta: " + sale.Folio + "\n" + (p.Cancelled ? "FUNCIÓN CANCELADA\n" : "") + service.Data.Settings.Legend)));
                    doc.Blocks.Add(section);
                }
            }
            else
            {
                decimal tax = sale.Total - sale.Total / (1 + sale.TaxRate / 100);
                doc.Blocks.Add(new Paragraph(new Run(service.Data.Settings.Theater + "\n" + service.Data.Settings.Address + "\nCOMPROBANTE · " + copy + "\n" + sale + "\n" + string.Join("\n", sale.Lines) + "\nTotal: " + sale.Total.ToString("C") + "\nImpuesto incluido (" + sale.TaxRate + "%): " + tax.ToString("C") + "\nEfectivo: " + sale.Cash.ToString("C") + "\nTarjeta: " + sale.Card.ToString("C") + "\n" + service.Data.Settings.Legend)));
            }
            dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, sale.Folio);
        }
        void BuildCatalog()
        {
            var page = Page("Funciones y productos");
            var functions = Ui.Table();
            functions.Height = 220;
            functions.ItemsSource = service.Data.Performances.OrderBy(p => p.Starts).ToList();
            page.Children.Add(functions);
            if (service.Admin)
                page.Children.Add(Ui.Row(Ui.Button("Nueva función", () => Run(() => EditPerformance(new Performance { Starts = DateTime.Today.AddDays(1).AddHours(19), Capacity = 100, Room = "Principal" }))), Ui.Button("Editar función", () => Run(() => EditPerformance(Codec.Clone(Selected<Performance>(functions)))))));
            page.Children.Add(Ui.Text("Productos de dulcería", 20));
            var inventory = Ui.Table();
            inventory.Height = 220;
            inventory.ItemsSource = service.Data.Products;
            page.Children.Add(inventory);
            if (service.Admin)
                page.Children.Add(Ui.Row(Ui.Button("Nuevo producto", () => Run(() => EditProduct(new Product()))), Ui.Button("Editar producto", () => Run(() => EditProduct(Codec.Clone(Selected<Product>(inventory)))))));
            page.Children.Add(Ui.Text("Las funciones canceladas dejan de venderse. Las ventas existentes se conservan y se reembolsan desde Ventas."));
            AddPage("Catálogo", page);
        }
        void EditPerformance(Performance p)
        {
            var f = new FormDialog(this, "Función / evento");
            f.Text("name", "Nombre", p.Name);
            f.Text("date", "Fecha y hora (dd/MM/aaaa HH:mm)", p.Starts.ToString("dd/MM/yyyy HH:mm"));
            f.Text("room", "Sala", p.Room);
            f.Text("capacity", "Capacidad de entrada general", p.Capacity.ToString());
            f.Text("adult", "Precio adulto", p.Adult.ToString());
            f.Text("child", "Precio niño", p.Child.ToString());
            f.Text("senior", "Precio adulto mayor", p.Senior.ToString());
            f.Text("description", "Descripción", p.Description);
            f.Text("restrictions", "Restricciones de edad / acceso", p.Restrictions);
            f.Text("image", "Ruta local de imagen (opcional)", p.ImagePath);
            f.Check("cancelled", "Función cancelada", p.Cancelled);
            f.Submit = () => { p.Name = f.Get("name"); p.Starts = f.Date("date"); p.Room = f.Get("room"); p.Capacity = f.Integer("capacity"); p.Adult = f.Number("adult"); p.Child = f.Number("child"); p.Senior = f.Number("senior"); p.Description = f.Get("description"); p.Restrictions = f.Get("restrictions"); p.ImagePath = f.Get("image"); p.Cancelled = f.Bool("cancelled"); TheaterService.Require(string.IsNullOrEmpty(p.ImagePath) || File.Exists(p.ImagePath), "La imagen indicada no existe."); service.SavePerformance(p); };
            if (f.ShowDialog() == true)
                Build();
        }
        void EditProduct(Product p)
        {
            var f = new FormDialog(this, "Producto");
            f.Text("name", "Nombre", p.Name);
            f.Text("price", "Precio de venta", p.Price.ToString());
            f.Text("minimum", "Alerta de inventario mínimo", p.Minimum.ToString());
            f.Check("active", "Activo", p.Enabled);
            f.Submit = () => { p.Name = f.Get("name"); p.Price = f.Number("price"); p.Minimum = f.Integer("minimum"); p.Enabled = f.Bool("active"); service.SaveProduct(p); };
            if (f.ShowDialog() == true)
                Build();
        }
        void BuildStock()
        {
            var page = Page("Inventario y mermas");
            var low = service.Data.Products.Where(p => p.Enabled && p.Stock <= p.Minimum).ToList();
            page.Children.Add(Ui.Text(low.Count == 0 ? "No hay productos bajo el mínimo." : "Reponer: " + string.Join(", ", low.Select(p => p.Name + " (" + p.Stock + ")")), 18));
            var table = Ui.Table();
            table.Height = 230;
            table.ItemsSource = service.Data.Products;
            page.Children.Add(table);
            page.Children.Add(Ui.Button("Registrar entrada / salida / merma", () => Run(() => { var p = Selected<Product>(table); var f = new FormDialog(this, "Ajustar inventario · " + p.Name); f.Text("quantity", "Cantidad (positiva entrada, negativa salida)"); f.Text("reason", "Motivo (compra, merma, ajuste...)"); f.Submit = () => service.AdjustStock(p.Id, f.Integer("quantity"), f.Get("reason")); if (f.ShowDialog() == true) Build(); })));
            page.Children.Add(Ui.Text("Historial de movimientos", 20));
            var movement = Ui.Table();
            movement.Height = 260;
            movement.ItemsSource = service.Data.StockMovements.OrderByDescending(m => m.Date).Select(m => new { Fecha = m.Date, Producto = service.Data.Products.Single(p => p.Id == m.ProductId).Name, Cantidad = m.Quantity, Motivo = m.Reason, Usuario = service.Data.Users.Single(u => u.Id == m.UserId).Name }).ToList();
            page.Children.Add(movement);
            AddPage("Inventario", page);
        }
        void BuildUsers()
        {
            var page = Page("Usuarios y permisos");
            page.Children.Add(Ui.Text("Cajero: vende y opera su turno. Supervisor: autoriza devoluciones, movimientos y descuentos; consulta control e inventario. Administrador: configura catálogos, promociones, usuarios y respaldos."));
            var table = Ui.Table();
            table.Height = 300;
            table.AutoGenerateColumns = false;
            foreach (var field in new[] { "Name", "Login", "Role", "Enabled" })
                table.Columns.Add(new DataGridTextColumn { Header = new Dictionary<string, string> { { "Name", "Nombre" }, { "Login", "Usuario" }, { "Role", "Rol" }, { "Enabled", "Activo" } }[field], Binding = new System.Windows.Data.Binding(field), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            table.ItemsSource = service.Data.Users;
            page.Children.Add(table);
            page.Children.Add(Ui.Row(Ui.Button("Nuevo usuario", () => Run(() => EditUser(new User()))), Ui.Button("Editar / desactivar", () => Run(() => EditUser(Codec.Clone(Selected<User>(table)))))));
            AddPage("Usuarios", page);
        }
        void EditUser(User u)
        {
            var f = new FormDialog(this, "Usuario");
            f.Text("name", "Nombre completo", u.Name);
            f.Text("login", "Usuario", u.Login);
            f.Choice("role", "Rol", Enum.GetNames(typeof(Role)), u.Role.ToString());
            f.Check("active", "Activo", u.Enabled);
            f.Secret("password", "Contraseña nueva (vacío conserva la actual)");
            f.Secret("confirm", "Repetir contraseña nueva");
            f.Submit = () => { TheaterService.Require(f.Get("password") == f.Get("confirm"), "Las contraseñas no coinciden."); u.Name = f.Get("name"); u.Login = f.Get("login"); u.Role = (Role)Enum.Parse(typeof(Role), f.Get("role")); u.Enabled = f.Bool("active"); service.SaveUser(u, f.Get("password")); };
            if (f.ShowDialog() == true)
                Build();
        }
        void ChangePassword()
        {
            var f = new FormDialog(this, "Cambiar contraseña");
            f.Secret("old", "Contraseña actual");
            f.Secret("new", "Contraseña nueva (mínimo 10 caracteres)");
            f.Secret("confirm", "Repetir contraseña nueva");
            f.Submit = () => { TheaterService.Require(f.Get("new") == f.Get("confirm"), "Las contraseñas no coinciden."); service.ChangePassword(f.Get("old"), f.Get("new")); };
            f.ShowDialog();
        }
        void BuildPromotions()
        {
            var page = Page("Promociones y cupones");
            page.Children.Add(Ui.Text("Porcentaje o monto sobre el carrito. El 2x1 aplica a pares de entradas de la misma función y tarifa. Una promoción por venta; descuentos por encima del umbral requieren autorización."));
            var table = Ui.Table();
            table.Height = 300;
            table.ItemsSource = service.Data.Promotions;
            page.Children.Add(table);
            page.Children.Add(Ui.Row(Ui.Button("Nueva promoción", () => Run(() => EditPromotion(new Promotion { From = DateTime.Today, Until = DateTime.Today.AddDays(30).AddHours(23).AddMinutes(59) }))), Ui.Button("Editar promoción", () => Run(() => EditPromotion(Codec.Clone(Selected<Promotion>(table)))))));
            AddPage("Promociones", page);
        }
        void EditPromotion(Promotion p)
        {
            var f = new FormDialog(this, "Promoción");
            f.Text("code", "Código del cupón", p.Code);
            f.Choice("kind", "Tipo", new[] { "Porcentaje", "Monto", "2x1" }, p.Kind);
            f.Text("value", "Valor (% o monto; ignorado para 2x1)", p.Value.ToString());
            f.Text("from", "Inicio (dd/MM/aaaa HH:mm)", p.From.ToString("dd/MM/yyyy HH:mm"));
            f.Text("until", "Fin (dd/MM/aaaa HH:mm)", p.Until.ToString("dd/MM/yyyy HH:mm"));
            f.Check("active", "Activa", p.Enabled);
            f.Submit = () => { p.Code = f.Get("code"); p.Kind = f.Get("kind"); p.Value = f.Number("value"); p.From = f.Date("from"); p.Until = f.Date("until"); p.Enabled = f.Bool("active"); service.SavePromotion(p); };
            if (f.ShowDialog() == true)
                Build();
        }
        void BuildReports()
        {
            var page = Page("Reportes de ventas");
            page.Children.Add(Ui.Text("Ingresos y devoluciones se contabilizan en su fecha de operación. Los cajeros consultan sus propias ventas; supervisores y administradores consultan todos los empleados."));
            from = new DatePicker { SelectedDate = DateTime.Today, Width = 150, Margin = new Thickness(0, 8, 12, 8) };
            to = new DatePicker { SelectedDate = DateTime.Today, Width = 150, Margin = new Thickness(0, 8, 12, 8) };
            reportKind = Combo(new[] { "Resumen diario", "Por función", "Por producto", "Por empleado", "Métodos de pago", "Ocupación" });
            reportKind.Width = 200;
            page.Children.Add(Ui.Row(from, to, reportKind, Ui.Button("Consultar", () => Run(GenerateReport)), Ui.Button("Exportar CSV", () => Run(ExportReport))));
            reportTable = Ui.Table();
            reportTable.Height = 440;
            page.Children.Add(reportTable);
            AddPage("Reportes", page);
        }
        void GenerateReport()
        {
            TheaterService.Require(from.SelectedDate.HasValue && to.SelectedDate.HasValue && from.SelectedDate <= to.SelectedDate, "Selecciona un rango de fechas válido.");
            DateTime start = from.SelectedDate.Value.Date, end = to.SelectedDate.Value.Date.AddDays(1);
            var all = service.Data.Sales.Where(s => service.Manager || s.UserId == service.Current.Id).ToList();
            var sales = all.Where(s => s.Date >= start && s.Date < end).ToList();
            var refunds = service.Data.Refunds.Where(r => r.Date >= start && r.Date < end && all.Any(s => s.Id == r.SaleId)).ToList();
            string kind = (string)reportKind.SelectedItem;
            reportRows = new List<string[]>();
            var rows = new List<ReportRow>();
            if (kind == "Métodos de pago")
            {
                rows.Add(new ReportRow { Concepto = "Efectivo", Ventas = sales.Sum(s => s.Cash), Devoluciones = refunds.Sum(r => r.Cash), Operaciones = sales.Count(s => s.Cash > 0) });
                rows.Add(new ReportRow { Concepto = "Tarjeta", Ventas = sales.Sum(s => s.Card), Devoluciones = refunds.Sum(r => r.Card), Operaciones = sales.Count(s => s.Card > 0) });
            }
            else if (kind == "Ocupación")
            {
                var occupancy = service.Data.Performances.Where(p => p.Starts >= start && p.Starts < end).OrderBy(p => p.Starts).Select(p => new { Funcion = p.Name, Fecha = p.Starts, Capacidad = p.Capacity, Vendidos = service.Sold(p.Id), Disponibles = service.Available(p.Id), Ocupacion = decimal.Round(100m * service.Sold(p.Id) / p.Capacity, 2), Cancelada = p.Cancelled }).ToList();
                reportTable.ItemsSource = occupancy;
                reportRows.Add(new[] { "Función", "Fecha", "Capacidad", "Vendidos", "Disponibles", "Ocupación %", "Cancelada" });
                reportRows.AddRange(occupancy.Select(p => new[] { p.Funcion, p.Fecha.ToString("g"), p.Capacidad.ToString(), p.Vendidos.ToString(), p.Disponibles.ToString(), p.Ocupacion.ToString(), p.Cancelada.ToString() }));
                return;
            }
            else
            {
                Func<Sale, SaleLine, string> key = (s, l) => kind == "Por función" ? l.ItemId : kind == "Por producto" ? l.ItemId : kind == "Por empleado" ? s.UserId : s.Date.ToString("yyyy-MM-dd");
                Func<Sale, SaleLine, string> label = (s, l) => kind == "Por función" ? service.Data.Performances.Single(p => p.Id == l.ItemId).Name : kind == "Por producto" ? service.Data.Products.Single(p => p.Id == l.ItemId).Name : kind == "Por empleado" ? s.Cashier : s.Date.ToString("dd/MM/yyyy");
                var map = new Dictionary<string, ReportRow>();
                foreach (var sale in sales)
                    foreach (var l in sale.Lines)
                    {
                        if (kind == "Por función" && !l.Ticket || kind == "Por producto" && l.Ticket)
                            continue;
                        string id = key(sale, l);
                        if (!map.ContainsKey(id))
                            map[id] = new ReportRow { Concepto = label(sale, l) };
                        map[id].Ventas += l.Total;
                        map[id].Unidades += l.Quantity;
                    }
                foreach (var r in refunds)
                {
                    var sale = all.Single(s => s.Id == r.SaleId);
                    var l = sale.Lines.Single(x => x.Id == r.LineId);
                    if (kind == "Por función" && !l.Ticket || kind == "Por producto" && l.Ticket)
                        continue;
                    string id = kind == "Resumen diario" ? r.Date.ToString("yyyy-MM-dd") : key(sale, l);
                    if (!map.ContainsKey(id))
                        map[id] = new ReportRow { Concepto = kind == "Resumen diario" ? r.Date.ToString("dd/MM/yyyy") : label(sale, l) };
                    map[id].Devoluciones += r.Amount;
                    map[id].Devueltas += r.Quantity;
                }
                foreach (var pair in map)
                {
                    pair.Value.Operaciones = sales.Count(s => s.Lines.Any(l => (kind != "Por función" || l.Ticket) && (kind != "Por producto" || !l.Ticket) && key(s, l) == pair.Key));
                }
                rows = map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
            }
            reportTable.ItemsSource = rows;
            reportRows.Add(new[] { "Concepto", "Operaciones", "Unidades", "Devueltas", "Ventas", "Devoluciones", "Neto" });
            reportRows.AddRange(rows.Select(r => new[] { r.Concepto, r.Operaciones.ToString(), r.Unidades.ToString(), r.Devueltas.ToString(), r.Ventas.ToString("F2"), r.Devoluciones.ToString("F2"), r.Neto.ToString("F2") }));
            Status.Text = "Ventas: " + sales.Sum(s => s.Total).ToString("C") + " · Devoluciones: " + refunds.Sum(r => r.Amount).ToString("C") + " · Transacciones: " + sales.Count;
        }
        public class ReportRow
        {
            public string Concepto
            {
                get; set;
            }
            public int Operaciones
            {
                get; set;
            }
            public int Unidades
            {
                get; set;
            }
            public int Devueltas
            {
                get; set;
            }
            public decimal Ventas
            {
                get; set;
            }
            public decimal Devoluciones
            {
                get; set;
            }
            public decimal Neto => Ventas - Devoluciones;
        }
        void ExportReport()
        {
            GenerateReport();
            var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "MiTeatro-reporte-" + DateTime.Today.ToString("yyyyMMdd") + ".csv" };
            if (dialog.ShowDialog() != true)
                return;
            File.WriteAllLines(dialog.FileName, reportRows.Select(r => string.Join(",", r.Select(Csv))), new UTF8Encoding(true));
        }
        static string Csv(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
                value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        void BuildAudit()
        {
            var page = Page("Bitácora de auditoría");
            var table = Ui.Table();
            table.Height = 480;
            table.ItemsSource = service.Data.Audits.OrderByDescending(a => a.Date).ToList();
            page.Children.Add(table);
            AddPage("Auditoría", page);
        }
        void BuildSettings()
        {
            var page = Page("Configuración y respaldos");
            page.Children.Add(Ui.Text(service.Data.Settings.Theater + "\nAlmacenamiento activo: " + service.Storage + "\nRespaldo automático: " + (string.IsNullOrWhiteSpace(service.Data.Settings.BackupFolder) ? "Sin carpeta configurada" : service.Data.Settings.BackupFolder), 18));
            page.Children.Add(Ui.Button("Datos del teatro y parámetros", () => Run(() => { var s = Codec.Clone(service.Data.Settings); var f = new FormDialog(this, "Configuración"); f.Text("name", "Nombre del teatro", s.Theater); f.Text("address", "Dirección", s.Address); f.Text("legend", "Leyenda del boleto", s.Legend); f.Text("tax", "Porcentaje de impuesto incluido en los precios", s.TaxRate.ToString()); f.Text("threshold", "Descuento máximo sin autorización ($)", s.DiscountApproval.ToString()); f.Text("timeout", "Bloqueo por inactividad (minutos)", s.TimeoutMinutes.ToString()); f.Text("backup", "Carpeta para respaldos automáticos", s.BackupFolder); f.Text("printer", "Nombre exacto de impresora (vacío usa predeterminada)", s.Printer); f.Submit = () => { s.Theater = f.Get("name"); s.Address = f.Get("address"); s.Legend = f.Get("legend"); s.TaxRate = f.Number("tax"); s.DiscountApproval = f.Number("threshold"); s.TimeoutMinutes = f.Integer("timeout"); s.BackupFolder = f.Get("backup"); s.Printer = f.Get("printer"); service.SaveSettings(s); }; if (f.ShowDialog() == true) Build(); })));
            page.Children.Add(Ui.Row(Ui.Button("Crear respaldo", () => Run(() => { var f = new SaveFileDialog { Filter = "Respaldo Mi Teatro|*.xml", FileName = "MiTeatro-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml" }; if (f.ShowDialog() == true) service.Backup(f.FileName); })), Ui.Button("Restaurar respaldo", () => Run(() => { TheaterService.Require(cart.Count == 0, "Vacía el carrito antes de restaurar."); var f = new OpenFileDialog { Filter = "Respaldo Mi Teatro|*.xml" }; if (f.ShowDialog() != true || !Confirm("La restauración sustituirá los datos activos. Se guardará una copia previa.\nNecesitarás las credenciales del respaldo. ¿Continuar?")) return; service.Backup(Path.Combine(folder, "pre-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml")); service.Restore(f.FileName); Authenticate(); }))));
            page.Children.Add(Ui.Text("Los respaldos automáticos se actualizan mientras la aplicación está abierta, con un archivo por día. Usa una carpeta en otra unidad o una carpeta sincronizada para conservar una copia externa."));
            page.Children.Add(Ui.Button("Programar respaldo diario a las 02:00", () => Run(ScheduleBackup)));
            page.Children.Add(Ui.Button("Configurar conexión MySQL", () => Run(ConfigureConnection)));
            page.Children.Add(Ui.Text("La conexión MySQL usa una base ya creada y un usuario con permisos de lectura, escritura y creación de la tabla del sistema. La contraseña se protege con la cuenta de Windows. Al cambiar a una base vacía se ofrece copiar los datos actuales; una base con datos nunca se sobrescribe automáticamente."));
            AddPage("Ajustes", page);
        }
        void ScheduleBackup()
        {
            TheaterService.Require(service.Admin, "Solo el administrador puede programar respaldos.");
            TheaterService.Require(!string.IsNullOrWhiteSpace(service.Data.Settings.BackupFolder), "Primero configura la carpeta de respaldos.");
            string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string taskCommand = "\\\"" + executable + "\\\" --backup";
            var start = new System.Diagnostics.ProcessStartInfo("schtasks.exe", "/Create /F /SC DAILY /ST 02:00 /TN MiTeatroDailyBackup /TR \"" + taskCommand + "\" /IT") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                TheaterService.Require(process.ExitCode == 0, "No se pudo programar el respaldo: " + error + output);
            }
            MessageBox.Show(this, "Respaldo diario programado a las 02:00. Windows debe estar encendido y tu sesión iniciada. Mantén la aplicación en esta ubicación.", "Mi Teatro");
        }
        void ConfigureConnection()
        {
            TheaterService.Require(service.Admin, "Solo el administrador puede cambiar la conexión.");
            TheaterService.Require(service.Active == null && cart.Count == 0, "Cierra caja y vacía el carrito antes de cambiar la conexión.");
            var c = Codec.Clone(connection);
            var f = new FormDialog(this, "Conexión de base de datos");
            f.Choice("mode", "Almacenamiento", new[] { "Archivo local", "MySQL" }, c.MySql ? "MySQL" : "Archivo local");
            f.Text("server", "Servidor", c.Server);
            f.Text("port", "Puerto", c.Port.ToString());
            f.Text("database", "Base de datos existente", c.Database);
            f.Text("user", "Usuario MySQL", c.User);
            f.Secret("password", "Contraseña MySQL (vacío conserva la guardada)");
            f.Check("tls", "Exigir conexión cifrada TLS", c.Tls);
            f.Submit = () => { c.MySql = f.Get("mode") == "MySQL"; c.Server = f.Get("server"); int port = f.Integer("port"); TheaterService.Require(port > 0 && port <= 65535, "Puerto inválido."); c.Port = (uint)port; c.Database = f.Get("database"); c.User = f.Get("user"); c.Tls = f.Bool("tls"); if (!string.IsNullOrEmpty(f.Get("password"))) c.SetPassword(f.Get("password")); IRepository target = c.MySql ? (IRepository)new MySqlRepository(c.ConnectionString()) : new LocalRepository(Path.Combine(folder, "theater.xml")); var targetData = target.Load(); TheaterService.Validate(targetData); if (targetData.Users.Count == 0 && Confirm("El destino está vacío. ¿Copiar los datos actuales al destino?")) { var copy = Codec.Clone(service.Data); copy.Revision = targetData.Revision + 1; target.Save(copy, targetData.Revision); } Codec.AtomicWrite(ConfigPath, Codec.Encode(c)); connection = c; service = new TheaterService(target); };
            if (f.ShowDialog() == true)
            {
                cart.Clear();
                Authenticate();
            }
        }
        void WindowClosing(object sender, CancelEventArgs e)
        {
            if (cart.Count > 0 && !authenticating && !Confirm("Hay una venta pendiente sin cobrar. ¿Salir y descartar el carrito?"))
            {
                e.Cancel = true;
                return;
            }
            timer?.Stop();
            SystemEvents.SessionSwitch -= SessionSwitch;
            System.Windows.Input.InputManager.Current.PreProcessInput -= TrackInput;
        }
    }
}




