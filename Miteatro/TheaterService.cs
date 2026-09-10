using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Miteatro
{
    public class TheaterService
    {
        readonly IRepository repository;
        public Database Data
        {
            get; private set;
        }
        public User Current
        {
            get; private set;
        }
        public string Storage => repository.Description;
        public Shift Active => Data.Shifts.SingleOrDefault(s => !s.Closed.HasValue);
        public bool Manager => Current != null && Current.Role != Role.Cajero;
        public bool Admin => Current != null && Current.Role == Role.Administrador;
        public string BackupError
        {
            get; private set;
        }
        int failed; DateTime retryAfter;
        public TheaterService(IRepository repository)
        {
            this.repository = repository;
            Data = repository.Load();
            Validate(Data);
        }
        public static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        public static void Money(decimal n)
        {
            Require(n >= 0 && n <= 10000000 && decimal.Round(n, 2) == n, "Monto inválido. Usa hasta dos decimales.");
        }
        static void Unique(IEnumerable<string> values, string entity)
        {
            var ids = values.ToList();
            Require(ids.All(x => !string.IsNullOrWhiteSpace(x)) && ids.Distinct().Count() == ids.Count, "Identificadores inválidos o duplicados en " + entity + ".");
        }
        public static void Validate(Database d)
        {
            Require(d != null && d.Schema == 1 && d.Revision >= 0, "Versión de datos incompatible.");
            Require(d.Users != null && d.Sales != null && d.Products != null && d.Performances != null && d.Shifts != null && d.Refunds != null && d.Tickets != null && d.Audits != null && d.Settings != null && d.Promotions != null && d.CashMovements != null && d.StockMovements != null, "Datos incompletos.");
            Unique(d.Users.Select(x => x.Id), "usuarios");
            Unique(d.Performances.Select(x => x.Id), "funciones");
            Unique(d.Products.Select(x => x.Id), "productos");
            Unique(d.Shifts.Select(x => x.Id), "turnos");
            Unique(d.Sales.Select(x => x.Id), "ventas");
            Unique(d.Refunds.Select(x => x.Id), "devoluciones");
            Unique(d.Promotions.Select(x => x.Id), "promociones");
            Unique(d.Audits.Select(x => x.Id), "auditoría");
            Unique(d.CashMovements.Select(x => x.Id), "movimientos de caja");
            Unique(d.StockMovements.Select(x => x.Id), "movimientos de inventario");
            Require(d.Users.All(u => !string.IsNullOrWhiteSpace(u.Login) && !string.IsNullOrWhiteSpace(u.Name) && !string.IsNullOrWhiteSpace(u.PasswordHash)), "Usuario incompleto.");
            Unique(d.Users.Select(u => u.Login.ToLowerInvariant()), "nombres de usuario");
            Require(d.Users.Count == 0 || d.Users.Any(u => u.Enabled && u.Role == Role.Administrador), "Debe existir un administrador activo.");
            Require(d.Shifts.Count(x => !x.Closed.HasValue) <= 1, "Hay más de un turno abierto.");
            foreach (var shift in d.Shifts)
            {
                Require(d.Users.Any(u => u.Id == shift.UserId), "Turno sin usuario.");
                Money(shift.Opening);
                Money(shift.Counted);
                Require(!shift.Closed.HasValue || shift.Closed.Value >= shift.Opened, "Fechas de turno inválidas.");
            }
            foreach (var p in d.Products)
            {
                Require(p.Stock >= 0 && p.Minimum >= 0 && !string.IsNullOrWhiteSpace(p.Name), "Inventario inválido.");
                Money(p.Price);
            }
            foreach (var p in d.Performances)
            {
                Require(p.Capacity > 0 && p.Capacity <= 100000 && !string.IsNullOrWhiteSpace(p.Name), "Función inválida.");
                Money(p.Adult);
                Money(p.Child);
                Money(p.Senior);
                Require(d.Tickets.Count(t => t.PerformanceId == p.Id && !t.Void) <= p.Capacity, "Boletos vendidos por encima del cupo.");
            }
            Unique(d.Sales.SelectMany(s => s.Lines).Select(l => l.Id), "detalles de venta");
            Unique(d.Sales.Select(s => s.Folio), "folios");
            Unique(d.Sales.Select(s => s.RequestId), "solicitudes de cobro");
            Unique(d.Tickets.Select(t => t.Code), "boletos");
            foreach (var sale in d.Sales)
            {
                Require(d.Users.Any(u => u.Id == sale.UserId) && d.Shifts.Any(x => x.Id == sale.ShiftId), "Venta con referencias inválidas.");
                Require(sale.Lines.Count > 0 && sale.Total == sale.Cash + sale.Card && sale.TaxRate >= 0 && sale.TaxRate <= 100, "Importes de venta inconsistentes.");
                Money(sale.Cash);
                Money(sale.Card);
                Money(sale.Total);
                var refunds = d.Refunds.Where(r => r.SaleId == sale.Id).ToList();
                Require(refunds.Sum(r => r.Cash) <= sale.Cash && refunds.Sum(r => r.Card) <= sale.Card, "Reembolso superior al pago recibido.");
                foreach (var l in sale.Lines)
                {
                    Require(l.Quantity > 0 && l.Returned >= 0 && l.Returned <= l.Quantity && l.Discount >= 0 && l.Discount <= l.Gross && (l.Ticket ? d.Performances.Any(p => p.Id == l.ItemId) : d.Products.Any(p => p.Id == l.ItemId)), "Detalle de venta inválido.");
                    Money(l.UnitPrice);
                    Money(l.Discount);
                    var returned = refunds.Where(r => r.LineId == l.Id).ToList();
                    Require(returned.Sum(r => r.Quantity) == l.Returned && returned.Sum(r => r.Amount) == decimal.Round(l.Total * l.Returned / l.Quantity, 2, MidpointRounding.AwayFromZero), "Devoluciones no coinciden con el detalle.");
                    if (l.Ticket)
                        Require(d.Tickets.Count(t => t.LineId == l.Id) == l.Quantity && d.Tickets.Count(t => t.LineId == l.Id && t.Void) == l.Returned, "Boletos inconsistentes con la venta.");
                }
            }
            foreach (var r in d.Refunds)
            {
                Require(r.Quantity > 0 && d.Sales.Any(s => s.Id == r.SaleId && s.Lines.Any(l => l.Id == r.LineId)) && d.Shifts.Any(s => s.Id == r.ShiftId) && d.Users.Any(u => u.Id == r.AuthorizedBy) && r.Amount == r.Cash + r.Card, "Devolución inválida.");
                Money(r.Amount);
                Money(r.Cash);
                Money(r.Card);
            }
            foreach (var t in d.Tickets)
                Require(!(t.Void && t.Used.HasValue) && d.Sales.Any(s => s.Id == t.SaleId && s.Lines.Any(l => l.Id == t.LineId && l.Ticket && l.ItemId == t.PerformanceId)), "Boleto inválido.");
            foreach (var m in d.CashMovements)
            {
                Require(d.Shifts.Any(s => s.Id == m.ShiftId) && d.Users.Any(u => u.Id == m.UserId), "Movimiento de caja sin referencia.");
                Money(Math.Abs(m.Amount));
            }
            foreach (var m in d.StockMovements)
                Require(d.Products.Any(p => p.Id == m.ProductId) && d.Users.Any(u => u.Id == m.UserId), "Movimiento de inventario sin referencia.");
            foreach (var p in d.Products)
                Require(d.StockMovements.Where(m => m.ProductId == p.Id).Sum(m => (long)m.Quantity) == p.Stock, "El inventario no coincide con sus movimientos.");
            Require(d.Settings.TimeoutMinutes >= 1 && d.Settings.TimeoutMinutes <= 120 && d.Settings.TaxRate >= 0 && d.Settings.TaxRate <= 100, "Configuración inválida.");
            Money(d.Settings.DiscountApproval);
        }
        void Signed()
        {
            Require(Current != null && Data.Users.Any(u => u.Id == Current.Id && u.Enabled), "Inicia sesión para continuar.");
        }
        void Manage()
        {
            Signed();
            Require(Manager, "Esta operación requiere supervisor o administrador.");
        }
        void Administrate()
        {
            Signed();
            Require(Admin, "Esta operación requiere administrador.");
        }
        void OwnShift()
        {
            Signed();
            Require(Active != null, "Abre caja antes de continuar.");
            Require(Active.UserId == Current.Id, "El turno pertenece a otro empleado. Debe cerrarlo o intervenir un supervisor.");
        }
        private void Change(string action, string detail, Action operation)
        {
            var before = Codec.Clone(Data);
            var userId = Current?.Id;
            try
            {
                operation();
                Validate(Data);
                Data.Audits.Add(new Audit { Date = DateTime.Now, User = Current?.Login ?? "Configuración inicial", Action = action, Detail = detail });
                Data.Revision = before.Revision + 1;
                try
                {
                    repository.Save(Data, before.Revision);
                }
                catch (Exception ex) { throw new InvalidOperationException("No fue posible confirmar el guardado. Reinicia y consulta el historial antes de repetir un cobro. " + ex.Message, ex); }
                Current = Data.Users.FirstOrDefault(u => u.Id == userId) ?? Current;
            }
            catch { Data = before; Current = Data.Users.FirstOrDefault(u => u.Id == userId); throw; }
            AutomaticBackup();
        }
        public void Setup(string name, string login, string password)
        {
            Require(Data.Users.Count == 0, "El sistema ya fue configurado.");
            Require(!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(login), "Completa nombre y usuario.");
            var hash = Passwords.Hash(password);
            Change("Configuración inicial", login, () => Data.Users.Add(new User { Name = name.Trim(), Login = login.Trim(), PasswordHash = hash, Role = Role.Administrador }));
        }
        public User Login(string login, string password)
        {
            Require(DateTime.Now >= retryAfter, "Demasiados intentos. Espera 30 segundos.");
            var u = Data.Users.FirstOrDefault(x => x.Enabled && string.Equals(x.Login, login.Trim(), StringComparison.OrdinalIgnoreCase));
            if (u == null || !Passwords.Verify(password, u.PasswordHash))
            {
                failed++;
                if (failed >= 5)
                {
                    retryAfter = DateTime.Now.AddSeconds(30);
                    failed = 0;
                }
                throw new InvalidOperationException("Usuario o contraseña incorrectos.");
            }
            failed = 0;
            Current = u;
            Change("Inicio de sesión", u.Login, () => { });
            return Current;
        }
        public void Logout()
        {
            Current = null;
        }
        public string Authorize(string login, string password)
        {
            Signed();
            Require(DateTime.Now >= retryAfter, "Espera 30 segundos antes de otro intento.");
            var u = Data.Users.FirstOrDefault(x => x.Enabled && x.Role != Role.Cajero && string.Equals(x.Login, login.Trim(), StringComparison.OrdinalIgnoreCase));
            if (u == null || !Passwords.Verify(password, u.PasswordHash))
            {
                failed++;
                if (failed >= 5)
                {
                    retryAfter = DateTime.Now.AddSeconds(30);
                    failed = 0;
                }
                throw new InvalidOperationException("Autorización inválida.");
            }
            failed = 0;
            authorized = u.Id;
            authorizationExpires = DateTime.Now.AddMinutes(2);
            return u.Id;
        }
        string authorized; DateTime authorizationExpires;
        void Approval(string id)
        {
            Signed();
            if (Manager && id == Current.Id)
                return;
            Require(id != null && id == authorized && DateTime.Now < authorizationExpires && Data.Users.Any(u => u.Id == id && u.Enabled && u.Role != Role.Cajero), "Se requiere autorización vigente de supervisor.");
            authorized = null;
        }
        public void SaveUser(User value, string password)
        {
            Administrate();
            Require(!string.IsNullOrWhiteSpace(value.Name) && !string.IsNullOrWhiteSpace(value.Login), "Completa nombre y usuario.");
            Require(!Data.Users.Any(u => u.Id != value.Id && string.Equals(u.Login, value.Login, StringComparison.OrdinalIgnoreCase)), "Ese usuario ya existe.");
            var old = Data.Users.FirstOrDefault(u => u.Id == value.Id);
            if (!string.IsNullOrEmpty(password))
                value.PasswordHash = Passwords.Hash(password);
            else
            {
                Require(old != null, "Define una contraseña.");
                value.PasswordHash = old.PasswordHash;
            }
            Require(value.Id != Current.Id || (value.Enabled && value.Role == Role.Administrador), "No puedes desactivar o quitarte el rol de administrador.");
            Require(Active == null || Active.UserId != value.Id || value.Enabled, "Cierra el turno del usuario antes de desactivarlo.");
            Change("Usuario actualizado", value.Login + " / " + value.Role + " / activo=" + value.Enabled, () => { Data.Users.RemoveAll(u => u.Id == value.Id); Data.Users.Add(Codec.Clone(value)); });
        }
        public void ChangePassword(string old, string next)
        {
            Signed();
            Require(Passwords.Verify(old, Current.PasswordHash), "Contraseña actual incorrecta.");
            var hash = Passwords.Hash(next);
            Change("Cambio de contraseña", Current.Login, () => Current.PasswordHash = hash);
        }
        public int Sold(string id) => Data.Tickets.Count(t => t.PerformanceId == id && !t.Void);
        public int Available(string id)
        {
            var p = Data.Performances.Single(x => x.Id == id);
            return p.Capacity - Sold(id);
        }
        public void SavePerformance(Performance value)
        {
            Administrate();
            Require(!string.IsNullOrWhiteSpace(value.Name) && !string.IsNullOrWhiteSpace(value.Room), "Completa nombre y sala.");
            Require(value.Capacity > 0 && value.Capacity <= 100000 && value.Capacity >= Sold(value.Id), "El cupo debe ser positivo y cubrir los boletos vendidos.");
            Money(value.Adult);
            Money(value.Child);
            Money(value.Senior);
            var old = Data.Performances.FirstOrDefault(x => x.Id == value.Id);
            Require(old != null || value.Starts > DateTime.Now, "La función nueva debe tener una fecha futura.");
            Change("Función actualizada", value.Name + " / " + value.Starts + " / cupo=" + value.Capacity + " / cancelada=" + value.Cancelled + " / tarifas=" + value.Adult + "," + value.Child + "," + value.Senior, () => { Data.Performances.RemoveAll(x => x.Id == value.Id); Data.Performances.Add(Codec.Clone(value)); });
        }
        public void SaveProduct(Product value)
        {
            Administrate();
            Require(!string.IsNullOrWhiteSpace(value.Name) && value.Minimum >= 0, "Nombre o mínimo inválido.");
            Money(value.Price);
            var old = Data.Products.FirstOrDefault(p => p.Id == value.Id);
            value.Stock = old?.Stock ?? 0;
            Change("Producto actualizado", value.Name + " / precio=" + value.Price + " / activo=" + value.Enabled, () => { Data.Products.RemoveAll(p => p.Id == value.Id); Data.Products.Add(Codec.Clone(value)); });
        }
        public void AdjustStock(string id, int quantity, string reason)
        {
            Manage();
            Require(quantity != 0 && !string.IsNullOrWhiteSpace(reason), "Ingresa cantidad distinta de cero y motivo.");
            var p = Data.Products.Single(x => x.Id == id);
            Require((long)p.Stock + quantity >= 0 && (long)p.Stock + quantity <= 10000000, "Existencias fuera de rango.");
            Change("Ajuste inventario", p.Name + " / " + quantity + " / " + reason, () => { p.Stock += quantity; Data.StockMovements.Add(new StockMovement { Date = DateTime.Now, ProductId = id, Quantity = quantity, Reason = reason, UserId = Current.Id }); });
        }
        public void SavePromotion(Promotion p)
        {
            Administrate();
            Require(!string.IsNullOrWhiteSpace(p.Code) && p.Until >= p.From, "Código o vigencia inválidos.");
            Require(new[] { "Porcentaje", "Monto", "2x1" }.Contains(p.Kind), "Tipo inválido.");
            Money(p.Value);
            Require(p.Kind != "Porcentaje" || p.Value <= 100, "El porcentaje no puede superar 100.");
            Require(!Data.Promotions.Any(x => x.Id != p.Id && string.Equals(x.Code, p.Code, StringComparison.OrdinalIgnoreCase)), "Código duplicado.");
            Change("Promoción actualizada", p.Code + " / " + p.Kind + " / " + p.Value, () => { Data.Promotions.RemoveAll(x => x.Id == p.Id); Data.Promotions.Add(Codec.Clone(p)); });
        }
        public void OpenShift(decimal cash)
        {
            Signed();
            Money(cash);
            Require(Active == null, "Ya hay un turno abierto.");
            Change("Apertura de caja", cash.ToString("C"), () => Data.Shifts.Add(new Shift { UserId = Current.Id, Cashier = Current.Name, Opened = DateTime.Now, Opening = cash }));
        }
        public decimal Expected(string shift) => Data.Shifts.Single(x => x.Id == shift).Opening + Data.Sales.Where(s => s.ShiftId == shift).Sum(s => s.Cash) - Data.Refunds.Where(r => r.ShiftId == shift).Sum(r => r.Cash) + Data.CashMovements.Where(m => m.ShiftId == shift).Sum(m => m.Amount);
        public void CloseShift(decimal counted)
        {
            Signed();
            Money(counted);
            Require(Active != null, "No hay turno abierto.");
            Require(Active.UserId == Current.Id || Manager, "Solo el cajero del turno o un supervisor pueden cerrarlo.");
            var shift = Active;
            var expected = Expected(shift.Id);
            Change("Corte de caja", shift.Cashier + " / esperado=" + expected + " / contado=" + counted, () => { shift.Closed = DateTime.Now; shift.Expected = expected; shift.Counted = counted; });
        }
        public void MoveCash(decimal amount, string reason, string approver)
        {
            OwnShift();
            Money(Math.Abs(amount));
            Require(amount != 0 && !string.IsNullOrWhiteSpace(reason), "Indica monto y motivo.");
            Approval(approver);
            Require(Expected(Active.Id) + amount >= 0, "El retiro supera el efectivo esperado.");
            Change("Movimiento caja", amount + " / " + reason + " / autoriza=" + approver, () => Data.CashMovements.Add(new CashMovement { ShiftId = Active.Id, Date = DateTime.Now, Amount = amount, Reason = reason, UserId = Current.Id }));
        }
        public SaleLine TicketLine(string id, string fare, int quantity)
        {
            OwnShift();
            Require(quantity > 0 && quantity <= 10000, "Cantidad inválida.");
            var p = Data.Performances.Single(x => x.Id == id);
            Require(!p.Cancelled && p.Starts > DateTime.Now, "La función no está disponible para venta.");
            Require(new[] { "Adulto", "Niño", "Adulto mayor" }.Contains(fare), "Tarifa inválida.");
            Require(Available(id) >= quantity, "Cupo insuficiente.");
            return new SaleLine { ItemId = id, Ticket = true, Name = p.Name, Fare = fare, Quantity = quantity, UnitPrice = fare == "Adulto" ? p.Adult : fare == "Niño" ? p.Child : p.Senior };
        }
        public SaleLine ProductLine(string id, int quantity)
        {
            OwnShift();
            Require(quantity > 0 && quantity <= 10000, "Cantidad inválida.");
            var p = Data.Products.Single(x => x.Id == id);
            Require(p.Enabled && p.Stock >= quantity, "Producto inactivo o sin existencias.");
            return new SaleLine { ItemId = id, Name = p.Name, Quantity = quantity, UnitPrice = p.Price };
        }
        public List<SaleLine> Quote(List<SaleLine> input, string code)
        {
            OwnShift();
            Require(input.Count > 0, "El carrito está vacío.");
            var lines = new List<SaleLine>();
            foreach (var l in input)
            {
                var fresh = l.Ticket ? TicketLine(l.ItemId, l.Fare, l.Quantity) : ProductLine(l.ItemId, l.Quantity);
                fresh.Id = l.Id;
                lines.Add(fresh);
            }
            foreach (var g in lines.GroupBy(l => new { l.Ticket, l.ItemId }))
            {
                int q = g.Sum(x => x.Quantity);
                Require(q <= (g.Key.Ticket ? Available(g.Key.ItemId) : Data.Products.Single(x => x.Id == g.Key.ItemId).Stock), "Cupo o inventario insuficiente para el carrito.");
            }
            if (string.IsNullOrWhiteSpace(code))
                return lines;
            var promo = Data.Promotions.FirstOrDefault(p => p.Enabled && string.Equals(p.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
            Require(promo != null && DateTime.Now >= promo.From && DateTime.Now <= promo.Until, "Promoción inexistente o fuera de vigencia.");
            decimal gross = lines.Sum(l => l.Gross);
            if (promo.Kind == "2x1")
            {
                foreach (var g in lines.Where(l => l.Ticket).GroupBy(l => new { l.ItemId, l.Fare, l.UnitPrice }))
                {
                    int free = g.Sum(l => l.Quantity) / 2;
                    foreach (var l in g)
                    {
                        int units = Math.Min(free, l.Quantity);
                        l.Discount = units * l.UnitPrice;
                        free -= units;
                    }
                }
            }
            else
            {
                decimal discount = promo.Kind == "Porcentaje" ? decimal.Round(gross * promo.Value / 100, 2, MidpointRounding.AwayFromZero) : Math.Min(gross, promo.Value);
                decimal remaining = discount;
                foreach (var l in lines)
                {
                    var part = l == lines.Last() ? remaining : Math.Min(remaining, decimal.Round(discount * (gross == 0 ? 0 : l.Gross / gross), 2, MidpointRounding.AwayFromZero));
                    l.Discount = Math.Min(l.Gross, part);
                    remaining -= l.Discount;
                }
                if (remaining > 0)
                    foreach (var l in lines)
                    {
                        var part = Math.Min(remaining, l.Gross - l.Discount);
                        l.Discount += part;
                        remaining -= part;
                    }
            }
            return lines;
        }
        public Sale Checkout(List<SaleLine> cart, string code, string method, decimal received, string reference, string approver, string requestId)
        {
            OwnShift();
            Require(!string.IsNullOrWhiteSpace(requestId), "Identificador de venta inválido.");
            var existing = Data.Sales.FirstOrDefault(s => s.RequestId == requestId);
            if (existing != null)
                return existing;
            var lines = Quote(cart, code);
            decimal discount = lines.Sum(l => l.Discount);
            if (discount > Data.Settings.DiscountApproval)
                Approval(approver);
            decimal total = lines.Sum(l => l.Total);
            Money(total);
            Money(received);
            Require(new[] { "Efectivo", "Tarjeta", "Mixto" }.Contains(method), "Método inválido.");
            decimal cash = method == "Efectivo" ? total : method == "Mixto" ? received : 0;
            decimal card = total - cash;
            if (method == "Efectivo")
                Require(received >= total, "Efectivo recibido insuficiente.");
            if (method == "Mixto")
                Require(received > 0 && received < total, "En pago mixto el efectivo debe estar entre cero y el total.");
            Require(card == 0 || !string.IsNullOrWhiteSpace(reference), "Indica la referencia del pago aprobado.");
            var sale = new Sale { Folio = "MT-" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant(), RequestId = requestId, Date = DateTime.Now, UserId = Current.Id, Cashier = Current.Name, ShiftId = Active.Id, Reference = card > 0 ? reference : "", Cash = cash, Card = card, Promotion = code, TaxRate = Data.Settings.TaxRate, Lines = lines };
            Change("Venta", sale.Folio + " / total=" + total + " / descuento=" + discount + " / autoriza=" + approver, () => { Data.Sales.Add(sale); foreach (var l in lines) { if (l.Ticket) { for (int i = 0; i < l.Quantity; i++) Data.Tickets.Add(new Ticket { Code = "B-" + Guid.NewGuid().ToString("N").ToUpperInvariant(), SaleId = sale.Id, LineId = l.Id, PerformanceId = l.ItemId }); } else { Data.Products.Single(p => p.Id == l.ItemId).Stock -= l.Quantity; Data.StockMovements.Add(new StockMovement { Date = DateTime.Now, ProductId = l.ItemId, Quantity = -l.Quantity, Reason = "Venta " + sale.Folio, UserId = Current.Id }); } } });
            return sale;
        }
        public decimal RefundAmount(SaleLine line, int quantity)
        {
            Require(quantity > 0 && quantity <= line.Quantity - line.Returned, "Cantidad a devolver inválida.");
            return decimal.Round(line.Total * (line.Returned + quantity) / line.Quantity, 2, MidpointRounding.AwayFromZero) - decimal.Round(line.Total * line.Returned / line.Quantity, 2, MidpointRounding.AwayFromZero);
        }
        public void Return(string saleId, string lineId, int quantity, bool full, bool restock, string reason, string reference, string approver)
        {
            OwnShift();
            Require(!string.IsNullOrWhiteSpace(reason), "Indica el motivo.");
            Approval(approver);
            var sale = Data.Sales.Single(x => x.Id == saleId);
            var selected = full ? sale.Lines.Where(l => l.Returned < l.Quantity).ToList() : sale.Lines.Where(l => l.Id == lineId).ToList();
            Require(selected.Count > 0, "No hay artículos pendientes de devolución.");
            var pending = new List<Refund>();
            decimal previously = Data.Refunds.Where(r => r.SaleId == saleId).Sum(r => r.Amount), cashBefore = Data.Refunds.Where(r => r.SaleId == saleId).Sum(r => r.Cash);
            foreach (var l in selected)
            {
                int q = full ? l.Quantity - l.Returned : quantity;
                decimal amount = RefundAmount(l, q);
                if (l.Ticket)
                    Require(Data.Tickets.Count(t => t.LineId == l.Id && !t.Void && !t.Used.HasValue) >= q, "No se pueden devolver entradas ya utilizadas.");
                decimal cumulative = previously + pending.Sum(r => r.Amount) + amount;
                decimal targetCash = sale.Total == 0 ? 0 : decimal.Round(cumulative * sale.Cash / sale.Total, 2, MidpointRounding.AwayFromZero);
                decimal cash = targetCash - cashBefore - pending.Sum(r => r.Cash);
                pending.Add(new Refund { SaleId = saleId, LineId = l.Id, ShiftId = Active.Id, Date = DateTime.Now, Quantity = q, Amount = amount, Cash = cash, Card = amount - cash, Reason = reason, AuthorizedBy = approver, Restock = restock, Reference = reference, Kind = full ? "Cancelación completa" : "Devolución parcial" });
            }
            Require(pending.Sum(r => r.Cash) <= Expected(Active.Id), "No hay efectivo suficiente en caja para la devolución.");
            Require(pending.Sum(r => r.Card) == 0 || !string.IsNullOrWhiteSpace(reference), "Registra la referencia del reembolso realizado en la terminal.");
            Change(full ? "Cancelación completa" : "Devolución parcial", sale.Folio + " / " + reason + " / autoriza=" + approver, () => { foreach (var r in pending) { var l = sale.Lines.Single(x => x.Id == r.LineId); l.Returned += r.Quantity; Data.Refunds.Add(r); if (l.Ticket) { foreach (var t in Data.Tickets.Where(t => t.LineId == l.Id && !t.Void && !t.Used.HasValue).Take(r.Quantity)) t.Void = true; } else if (restock) { Data.Products.Single(p => p.Id == l.ItemId).Stock += r.Quantity; Data.StockMovements.Add(new StockMovement { Date = DateTime.Now, ProductId = l.ItemId, Quantity = r.Quantity, Reason = "Devolución " + sale.Folio, UserId = Current.Id }); } } });
        }
        public void UseTicket(string code)
        {
            Signed();
            var t = Data.Tickets.FirstOrDefault(x => x.Code == code.Trim());
            Require(t != null && !t.Void && !t.Used.HasValue, "Boleto inexistente, anulado o ya utilizado.");
            var p = Data.Performances.Single(x => x.Id == t.PerformanceId);
            Require(!p.Cancelled && p.Starts.Date == DateTime.Today, "El boleto no corresponde a una función activa de hoy.");
            Change("Entrada validada", code, () => t.Used = DateTime.Now);
        }
        public void PrintRecorded(string saleId, bool tickets)
        {
            Signed();
            var sale = Data.Sales.Single(s => s.Id == saleId);
            Change("Impresión solicitada", sale.Folio + (tickets ? " / boletos" : " / comprobante"), () => { if (tickets) sale.TicketPrints++; else sale.Prints++; });
        }
        public void SaveSettings(Settings value)
        {
            Administrate();
            Require(!string.IsNullOrWhiteSpace(value.Theater), "Indica el nombre del teatro.");
            Money(value.DiscountApproval);
            Require(value.TaxRate >= 0 && value.TaxRate <= 100 && value.TimeoutMinutes >= 1 && value.TimeoutMinutes <= 120, "Impuesto o tiempo de sesión inválido.");
            Change("Configuración actualizada", value.Theater, () => Data.Settings = Codec.Clone(value));
        }
        public void Backup(string path)
        {
            Administrate();
            Codec.AtomicWrite(path, Codec.Encode(Data));
            Change("Respaldo creado", path, () => { });
        }
        public void Restore(string path)
        {
            Administrate();
            Require(Active == null, "Cierra caja antes de restaurar.");
            var restored = Codec.Decode<Database>(File.ReadAllText(path));
            Validate(restored);
            Require(restored.Users.Any(u => u.Enabled && u.Role == Role.Administrador), "El respaldo no contiene administrador.");
            var current = Data;
            Change("Respaldo restaurado", path, () => { restored.Revision = current.Revision; Data = restored; });
            Current = null;
        }
        public void AutomaticBackup()
        {
            try
            {
                var folder = Data.Settings.BackupFolder;
                if (string.IsNullOrWhiteSpace(folder))
                    return;
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "MiTeatro-" + DateTime.Today.ToString("yyyy-MM-dd") + ".xml");
                Codec.AtomicWrite(path, Codec.Encode(Data));
                BackupError = null;
            }
            catch (Exception ex) { BackupError = "Falló el respaldo automático: " + ex.Message; }
        }
    }
}



