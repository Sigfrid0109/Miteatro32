using System;
using System.Collections.Generic;
using System.Linq;
namespace Miteatro
{
    public enum Role
    {
        Cajero, Supervisor, Administrador
    }
    public class User
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string Name
        {
            get; set;
        }
        public string Login
        {
            get; set;
        }
        public string PasswordHash
        {
            get; set;
        }
        public Role Role
        {
            get; set;
        }
        public bool Enabled { get; set; } = true; public override string ToString() => Name + " · " + Role;
    }
    public class Performance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string Name
        {
            get; set;
        }
        public DateTime Starts
        {
            get; set;
        }
        public string Room
        {
            get; set;
        }
        public int Capacity
        {
            get; set;
        }
        public decimal Adult
        {
            get; set;
        }
        public decimal Child
        {
            get; set;
        }
        public decimal Senior
        {
            get; set;
        }
        public string Description
        {
            get; set;
        }
        public string Restrictions
        {
            get; set;
        }
        public string ImagePath
        {
            get; set;
        }
        public bool Cancelled
        {
            get; set;
        }
        public override string ToString() => Name + " · " + Starts.ToString("dd/MM/yyyy HH:mm") + (Cancelled ? " · CANCELADA" : "");
    }
    public class Product
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string Name
        {
            get; set;
        }
        public decimal Price
        {
            get; set;
        }
        public int Stock
        {
            get; set;
        }
        public int Minimum
        {
            get; set;
        }
        public bool Enabled { get; set; } = true; public override string ToString() => Name + " · " + Price.ToString("C") + " · Stock " + Stock;
    }
    public class Promotion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string Code
        {
            get; set;
        }
        public string Kind { get; set; } = "Porcentaje"; public decimal Value
        {
            get; set;
        }
        public DateTime From
        {
            get; set;
        }
        public DateTime Until
        {
            get; set;
        }
        public bool Enabled { get; set; } = true; public override string ToString() => Code + " · " + Kind + " · " + Value;
    }
    public class SaleLine
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string ItemId
        {
            get; set;
        }
        public bool Ticket
        {
            get; set;
        }
        public string Name
        {
            get; set;
        }
        public string Fare
        {
            get; set;
        }
        public int Quantity
        {
            get; set;
        }
        public int Returned
        {
            get; set;
        }
        public decimal UnitPrice
        {
            get; set;
        }
        public decimal Discount
        {
            get; set;
        }
        public decimal Gross => Quantity * UnitPrice; public decimal Total => Gross - Discount; public override string ToString() => Name + " · " + Quantity + " × " + UnitPrice.ToString("C") + " · Neto " + Total.ToString("C") + " · Devueltos " + Returned;
    }
    public class Ticket
    {
        public string Code
        {
            get; set;
        }
        public string SaleId
        {
            get; set;
        }
        public string LineId
        {
            get; set;
        }
        public string PerformanceId
        {
            get; set;
        }
        public bool Void
        {
            get; set;
        }
        public DateTime? Used
        {
            get; set;
        }
        public override string ToString() => Code + (Void ? " · ANULADO" : Used.HasValue ? " · UTILIZADO" : " · VÁLIDO");
    }
    public class Sale
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string Folio
        {
            get; set;
        }
        public string RequestId
        {
            get; set;
        }
        public DateTime Date
        {
            get; set;
        }
        public string UserId
        {
            get; set;
        }
        public string Cashier
        {
            get; set;
        }
        public string ShiftId
        {
            get; set;
        }
        public string Reference
        {
            get; set;
        }
        public string Promotion
        {
            get; set;
        }
        public decimal Cash
        {
            get; set;
        }
        public decimal Card
        {
            get; set;
        }
        public decimal TaxRate
        {
            get; set;
        }
        public int Prints
        {
            get; set;
        }
        public int TicketPrints
        {
            get; set;
        }
        public List<SaleLine> Lines { get; set; } = new List<SaleLine>(); public decimal Total => Lines.Sum(l => l.Total); public string Status => Lines.All(l => l.Returned == l.Quantity) ? "Devuelta / cancelada" : Lines.Any(l => l.Returned > 0) ? "Devolución parcial" : "Pagada"; public override string ToString() => Folio + " · " + Date.ToString("g") + " · " + Cashier + " · " + Total.ToString("C") + " · " + Status;
    }
    public class Refund
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string SaleId
        {
            get; set;
        }
        public string LineId
        {
            get; set;
        }
        public string ShiftId
        {
            get; set;
        }
        public DateTime Date
        {
            get; set;
        }
        public int Quantity
        {
            get; set;
        }
        public decimal Amount
        {
            get; set;
        }
        public decimal Cash
        {
            get; set;
        }
        public decimal Card
        {
            get; set;
        }
        public string Reason
        {
            get; set;
        }
        public string AuthorizedBy
        {
            get; set;
        }
        public bool Restock
        {
            get; set;
        }
        public string Reference
        {
            get; set;
        }
        public string Kind
        {
            get; set;
        }
    }
    public class Shift
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string UserId
        {
            get; set;
        }
        public string Cashier
        {
            get; set;
        }
        public DateTime Opened
        {
            get; set;
        }
        public DateTime? Closed
        {
            get; set;
        }
        public decimal Opening
        {
            get; set;
        }
        public decimal Counted
        {
            get; set;
        }
        public decimal Expected
        {
            get; set;
        }
        public decimal Difference => Counted - Expected; public override string ToString() => Opened.ToString("g") + " · " + Cashier + " · " + (Closed.HasValue ? "Cerrada · Diferencia " + Difference.ToString("C") : "Abierta");
    }
    public class CashMovement
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string ShiftId
        {
            get; set;
        }
        public DateTime Date
        {
            get; set;
        }
        public decimal Amount
        {
            get; set;
        }
        public string Reason
        {
            get; set;
        }
        public string UserId
        {
            get; set;
        }
    }
    public class StockMovement
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public string ProductId
        {
            get; set;
        }
        public DateTime Date
        {
            get; set;
        }
        public int Quantity
        {
            get; set;
        }
        public string Reason
        {
            get; set;
        }
        public string UserId
        {
            get; set;
        }
    }
    public class Audit
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); public DateTime Date
        {
            get; set;
        }
        public string User
        {
            get; set;
        }
        public string Action
        {
            get; set;
        }
        public string Detail
        {
            get; set;
        }
    }
    public class Settings
    {
        public string Theater { get; set; } = "Mi Teatro"; public string Address { get; set; } = ""; public string Legend { get; set; } = "Gracias por tu visita. Conserva tu boleto."; public decimal TaxRate { get; set; } = 0; public decimal DiscountApproval { get; set; } = 100; public int TimeoutMinutes { get; set; } = 5; public string BackupFolder { get; set; } = ""; public string Printer { get; set; } = "";
    }
    public class Database
    {
        public int Schema { get; set; } = 1; public long Revision
        {
            get; set;
        }
        public List<User> Users { get; set; } = new List<User>(); public List<Performance> Performances { get; set; } = new List<Performance>(); public List<Product> Products { get; set; } = new List<Product>(); public List<Promotion> Promotions { get; set; } = new List<Promotion>(); public List<Sale> Sales { get; set; } = new List<Sale>(); public List<Refund> Refunds { get; set; } = new List<Refund>(); public List<Ticket> Tickets { get; set; } = new List<Ticket>(); public List<Shift> Shifts { get; set; } = new List<Shift>(); public List<CashMovement> CashMovements { get; set; } = new List<CashMovement>(); public List<StockMovement> StockMovements { get; set; } = new List<StockMovement>(); public List<Audit> Audits { get; set; } = new List<Audit>(); public Settings Settings { get; set; } = new Settings();
    }
}

