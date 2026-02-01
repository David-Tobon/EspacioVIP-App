using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Espacio_VIP_SL_App.Data.Stores
{
    public sealed class SuppliersStore : ICrudStore
    {
        private const string Table = "Proveedores";

        public DataTable Load()
        {
            using var conn = Data.AppDb.Open();
            EnsureSchema(conn);

            var dt = NewTable();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT id, name, tax_id, phone, email, address, notes FROM {Table} ORDER BY id;";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                dt.Rows.Add(
                    r.GetInt64(0).ToString(),
                    r.IsDBNull(1) ? "" : r.GetString(1),
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    r.IsDBNull(3) ? "" : r.GetString(3),
                    r.IsDBNull(4) ? "" : r.GetString(4),
                    r.IsDBNull(5) ? "" : r.GetString(5),
                    r.IsDBNull(6) ? "" : r.GetString(6)
                );
            }

            dt.AcceptChanges();
            return dt;
        }

        public void Save(DataTable table)
        {
            using var conn = Data.AppDb.Open();
            EnsureSchema(conn);

            using var tx = conn.BeginTransaction();

            // 1) Deletes
            foreach (DataRow row in table.Rows)
            {
                if (row.RowState != DataRowState.Deleted) continue;

                var id = Convert.ToInt64(row["Id", DataRowVersion.Original]?.ToString() ?? "0");
                if (id <= 0) continue;

                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = $"DELETE FROM {Table} WHERE id=@id;";
                del.Parameters.AddWithValue("@id", id);
                del.ExecuteNonQuery();
            }

            // 2) Inserts/Updates
            foreach (DataRow row in table.Rows)
            {
                if (row.RowState == DataRowState.Unchanged || row.RowState == DataRowState.Deleted)
                    continue;

                var name = (row["Nombre"]?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue; // no guardamos registros “vacíos”

                bool hasId = long.TryParse(row["Id"]?.ToString(), out long id) && id > 0;

                if (!hasId)
                {
                    using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText =
                        $"INSERT INTO {Table}(name, tax_id, phone, email, address, notes) " +
                        $"VALUES(@name,@tax,@phone,@email,@addr,@notes);" +
                        $"SELECT last_insert_rowid();";

                    BindCommonParams(ins, row);
                    var newId = (long)(ins.ExecuteScalar() ?? 0);
                    row["Id"] = newId.ToString();
                }
                else
                {
                    using var upd = conn.CreateCommand();
                    upd.Transaction = tx;
                    upd.CommandText =
                        $"UPDATE {Table} SET " +
                        $"name=@name, tax_id=@tax, phone=@phone, email=@email, address=@addr, notes=@notes " +
                        $"WHERE id=@id;";

                    BindCommonParams(upd, row);
                    upd.Parameters.AddWithValue("@id", id);
                    upd.ExecuteNonQuery();
                }
            }

            tx.Commit();
            table.AcceptChanges();
        }

        private static void BindCommonParams(SqliteCommand cmd, DataRow row)
        {
            cmd.Parameters.AddWithValue("@name", (row["Nombre"]?.ToString() ?? "").Trim());
            cmd.Parameters.AddWithValue("@tax", NullIfEmpty(row["NIF/CIF"]));
            cmd.Parameters.AddWithValue("@phone", NullIfEmpty(row["Teléfono"]));
            cmd.Parameters.AddWithValue("@email", NullIfEmpty(row["Email"]));
            cmd.Parameters.AddWithValue("@addr", NullIfEmpty(row["Dirección"]));
            cmd.Parameters.AddWithValue("@notes", NullIfEmpty(row["Notas"]));
        }

        private static object NullIfEmpty(object? value)
        {
            var s = (value?.ToString() ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? DBNull.Value : s;
        }

        private static DataTable NewTable()
        {
            var dt = new DataTable("Proveedores");
            dt.Columns.Add("Id", typeof(string));
            dt.Columns.Add("Nombre", typeof(string));
            dt.Columns.Add("NIF/CIF", typeof(string));
            dt.Columns.Add("Teléfono", typeof(string));
            dt.Columns.Add("Email", typeof(string));
            dt.Columns.Add("Dirección", typeof(string));
            dt.Columns.Add("Notas", typeof(string));
            return dt;
        }

        private static void EnsureSchema(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"CREATE TABLE IF NOT EXISTS {Table} (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "name TEXT NOT NULL," +
                "tax_id TEXT NULL," +
                "phone TEXT NULL," +
                "email TEXT NULL," +
                "address TEXT NULL," +
                "category TEXT NULL," +
                "notes TEXT NULL," +
                "created_at TEXT NOT NULL DEFAULT (datetime('now'))," +
                "updated_at TEXT NULL" +
                ");";
            cmd.ExecuteNonQuery();
        }

    }
}
