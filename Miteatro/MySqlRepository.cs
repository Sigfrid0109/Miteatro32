using System;
using System.Collections.Generic;
using System.Linq;
using MySql.Data.MySqlClient;
namespace Miteatro
{
    // Entity payloads preserve the complete versioned model; relational keys enforce links.
    // A revision lock and one InnoDB transaction commit the entire cash register operation.
    public class MySqlRepository : IRepository
    {
        readonly string connection;
        static readonly string[] Tables = { "users", "performances", "products", "promotions", "shifts", "sales", "lines", "refunds", "tickets", "cash_movements", "stock_movements", "audits" };
        public string Description => "MySQL";
        MySqlConnection Open()
        {
            var c = new MySqlConnection(connection);
            c.Open();
            return c;
        }
        static void Exec(MySqlConnection c, MySqlTransaction tx, string sql)
        {
            using (var cmd = new MySqlCommand(sql, c, tx))
                cmd.ExecuteNonQuery();
        }
        public MySqlRepository(string connection)
        {
            this.connection = connection;
            using (var c = Open())
            {
                Exec(c, null, "CREATE TABLE IF NOT EXISTS miteatro_state (id INT PRIMARY KEY, revision BIGINT NOT NULL, payload LONGTEXT NOT NULL) ENGINE=InnoDB");
                Create(c, "users", "");
                Create(c, "performances", "");
                Create(c, "products", "");
                Create(c, "promotions", "");
                Create(c, "shifts", ", user_id VARCHAR(64) NOT NULL, FOREIGN KEY(user_id) REFERENCES mt_users(id)");
                Create(c, "sales", ", user_id VARCHAR(64) NOT NULL, shift_id VARCHAR(64) NOT NULL, FOREIGN KEY(user_id) REFERENCES mt_users(id), FOREIGN KEY(shift_id) REFERENCES mt_shifts(id)");
                Create(c, "lines", ", sale_id VARCHAR(64) NOT NULL, performance_id VARCHAR(64) NULL, product_id VARCHAR(64) NULL, FOREIGN KEY(sale_id) REFERENCES mt_sales(id), FOREIGN KEY(performance_id) REFERENCES mt_performances(id), FOREIGN KEY(product_id) REFERENCES mt_products(id)");
                Create(c, "refunds", ", sale_id VARCHAR(64) NOT NULL, line_id VARCHAR(64) NOT NULL, shift_id VARCHAR(64) NOT NULL, user_id VARCHAR(64) NOT NULL, FOREIGN KEY(sale_id) REFERENCES mt_sales(id), FOREIGN KEY(line_id) REFERENCES mt_lines(id), FOREIGN KEY(shift_id) REFERENCES mt_shifts(id), FOREIGN KEY(user_id) REFERENCES mt_users(id)");
                Create(c, "tickets", ", sale_id VARCHAR(64) NOT NULL, line_id VARCHAR(64) NOT NULL, performance_id VARCHAR(64) NOT NULL, FOREIGN KEY(sale_id) REFERENCES mt_sales(id), FOREIGN KEY(line_id) REFERENCES mt_lines(id), FOREIGN KEY(performance_id) REFERENCES mt_performances(id)");
                Create(c, "cash_movements", ", shift_id VARCHAR(64) NOT NULL, user_id VARCHAR(64) NOT NULL, FOREIGN KEY(shift_id) REFERENCES mt_shifts(id), FOREIGN KEY(user_id) REFERENCES mt_users(id)");
                Create(c, "stock_movements", ", product_id VARCHAR(64) NOT NULL, user_id VARCHAR(64) NOT NULL, FOREIGN KEY(product_id) REFERENCES mt_products(id), FOREIGN KEY(user_id) REFERENCES mt_users(id)");
                Create(c, "audits", "");
                using (var cmd = new MySqlCommand("INSERT IGNORE INTO miteatro_state(id,revision,payload) VALUES(1,0,@p)", c))
                {
                    cmd.Parameters.AddWithValue("@p", Codec.Encode(new Database()));
                    cmd.ExecuteNonQuery();
                }
            }
        }
        static void Create(MySqlConnection c, string table, string keys)
        {
            Exec(c, null, "CREATE TABLE IF NOT EXISTS mt_" + table + " (id VARCHAR(64) PRIMARY KEY,payload LONGTEXT NOT NULL" + keys + ") ENGINE=InnoDB");
        }
        static List<T> Read<T>(MySqlConnection c, MySqlTransaction tx, string table)
        {
            var items = new List<T>();
            using (var cmd = new MySqlCommand("SELECT payload FROM mt_" + table + " ORDER BY id", c, tx))
            using (var reader = cmd.ExecuteReader())
                while (reader.Read())
                    items.Add(Codec.Decode<T>(reader.GetString(0)));
            return items;
        }
        public Database Load()
        {
            using (var c = Open())
            using (var tx = c.BeginTransaction(System.Data.IsolationLevel.RepeatableRead))
            {
                Database d;
                using (var cmd = new MySqlCommand("SELECT payload FROM miteatro_state WHERE id=1", c, tx))
                    d = Codec.Decode<Database>((string)cmd.ExecuteScalar());
                d.Users = Read<User>(c, tx, "users");
                d.Performances = Read<Performance>(c, tx, "performances");
                d.Products = Read<Product>(c, tx, "products");
                d.Promotions = Read<Promotion>(c, tx, "promotions");
                d.Shifts = Read<Shift>(c, tx, "shifts");
                d.Sales = Read<Sale>(c, tx, "sales");
                d.Refunds = Read<Refund>(c, tx, "refunds");
                d.Tickets = Read<Ticket>(c, tx, "tickets");
                d.CashMovements = Read<CashMovement>(c, tx, "cash_movements");
                d.StockMovements = Read<StockMovement>(c, tx, "stock_movements");
                d.Audits = Read<Audit>(c, tx, "audits");
                tx.Commit();
                return d;
            }
        }
        class Row
        {
            public string Table, Id, Payload; public string[] Keys;
        }
        static void Add<T>(List<Row> rows, string table, string id, T item, params string[] keys)
        {
            rows.Add(new Row { Table = table, Id = id, Payload = Codec.Encode(item), Keys = keys });
        }
        static void Upsert(MySqlConnection c, MySqlTransaction tx, Row row)
        {
            var columns = new List<string> { "id", "payload" };
            var parameters = new List<string> { "@id", "@payload" };
            var updates = new List<string> { "payload=@payload" };
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.Parameters.AddWithValue("@id", row.Id);
                cmd.Parameters.AddWithValue("@payload", row.Payload);
                for (int i = 0; i < row.Keys.Length; i += 2)
                {
                    columns.Add(row.Keys[i]);
                    parameters.Add("@k" + i);
                    updates.Add(row.Keys[i] + "=@k" + i);
                    cmd.Parameters.AddWithValue("@k" + i, (object)row.Keys[i + 1] ?? DBNull.Value);
                }
                cmd.CommandText = "INSERT INTO mt_" + row.Table + " (" + string.Join(",", columns) + ") VALUES (" + string.Join(",", parameters) + ") ON DUPLICATE KEY UPDATE " + string.Join(",", updates);
                cmd.ExecuteNonQuery();
            }
        }
        public void Save(Database db, long expected)
        {
            var rows = new List<Row>();
            foreach (var x in db.Users)
                Add(rows, "users", x.Id, x);
            foreach (var x in db.Performances)
                Add(rows, "performances", x.Id, x);
            foreach (var x in db.Products)
                Add(rows, "products", x.Id, x);
            foreach (var x in db.Promotions)
                Add(rows, "promotions", x.Id, x);
            foreach (var x in db.Shifts)
                Add(rows, "shifts", x.Id, x, "user_id", x.UserId);
            foreach (var x in db.Sales)
            {
                Add(rows, "sales", x.Id, x, "user_id", x.UserId, "shift_id", x.ShiftId);
                foreach (var l in x.Lines)
                    Add(rows, "lines", l.Id, l, "sale_id", x.Id, "performance_id", l.Ticket ? l.ItemId : null, "product_id", l.Ticket ? null : l.ItemId);
            }
            foreach (var x in db.Refunds)
                Add(rows, "refunds", x.Id, x, "sale_id", x.SaleId, "line_id", x.LineId, "shift_id", x.ShiftId, "user_id", x.AuthorizedBy);
            foreach (var x in db.Tickets)
                Add(rows, "tickets", x.Code, x, "sale_id", x.SaleId, "line_id", x.LineId, "performance_id", x.PerformanceId);
            foreach (var x in db.CashMovements)
                Add(rows, "cash_movements", x.Id, x, "shift_id", x.ShiftId, "user_id", x.UserId);
            foreach (var x in db.StockMovements)
                Add(rows, "stock_movements", x.Id, x, "product_id", x.ProductId, "user_id", x.UserId);
            foreach (var x in db.Audits)
                Add(rows, "audits", x.Id, x);
            using (var c = Open())
            using (var tx = c.BeginTransaction())
            {
                var metadata = new Database { Schema = db.Schema, Revision = db.Revision, Settings = db.Settings };
                using (var cmd = new MySqlCommand("UPDATE miteatro_state SET payload=@p,revision=@n WHERE id=1 AND revision=@e", c, tx))
                {
                    cmd.Parameters.AddWithValue("@p", Codec.Encode(metadata));
                    cmd.Parameters.AddWithValue("@n", db.Revision);
                    cmd.Parameters.AddWithValue("@e", expected);
                    if (cmd.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Los datos cambiaron en otra instancia. Reinicia antes de continuar.");
                }
                var old = new Dictionary<string, Dictionary<string, string>>();
                foreach (var table in Tables)
                {
                    var records = new Dictionary<string, string>();
                    using (var cmd = new MySqlCommand("SELECT id,payload FROM mt_" + table, c, tx))
                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            records.Add(reader.GetString(0), reader.GetString(1));
                    old.Add(table, records);
                }
                // Delete only records absent in a restored snapshot, children before parents.
                foreach (var table in Tables.Reverse())
                {
                    var ids = new HashSet<string>(rows.Where(r => r.Table == table).Select(r => r.Id));
                    foreach (var id in old[table].Keys.Where(id => !ids.Contains(id)))
                        using (var cmd = new MySqlCommand("DELETE FROM mt_" + table + " WHERE id=@id", c, tx))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.ExecuteNonQuery();
                        }
                }
                foreach (var row in rows)
                {
                    string previous;
                    if (!old[row.Table].TryGetValue(row.Id, out previous) || previous != row.Payload)
                        Upsert(c, tx, row);
                }
                tx.Commit();
            }
        }
    }
}
