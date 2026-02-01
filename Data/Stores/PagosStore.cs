using System;
using System.Data;
using Microsoft.Data.Sqlite;
using Espacio_VIP_SL_App.Utils;

namespace Espacio_VIP_SL_App.Data.Stores
{
    public sealed class PagosStore
    {
        public DataTable Load()
        {
            using var conn = Data.AppDb.Open();
            var dt = NewTable();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT " +
                "p.id, p.client_id, c.name, p.trabajo_id, COALESCE(t.title,''), " +
                "p.doc_no, p.amount_cents, p.paid_at, p.method, p.reference, p.notes, " +
                "COALESCE(SUM(pa.amount_cents),0) AS applied_cents " +
                "FROM Pagos p " +
                "JOIN Clientes c ON c.id = p.client_id " +
                "LEFT JOIN Trabajos t ON t.id = p.trabajo_id " +
                "LEFT JOIN PagoAplicaciones pa ON pa.payment_id = p.id " +
                "GROUP BY p.id " +
                "ORDER BY p.paid_at DESC, p.id DESC;";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long id = r.GetInt64(0);
                long clientId = r.GetInt64(1);
                string clientName = r.IsDBNull(2) ? "" : r.GetString(2);
                long trabajoId = r.IsDBNull(3) ? 0 : r.GetInt64(3);
                string obra = r.IsDBNull(4) ? "" : r.GetString(4);

                string docNo = r.IsDBNull(5) ? "" : r.GetString(5);

                long amount = r.IsDBNull(6) ? 0 : r.GetInt64(6);
                string paidAtDb = r.IsDBNull(7) ? "" : r.GetString(7);

                string method = r.IsDBNull(8) ? "" : r.GetString(8);
                string reference = r.IsDBNull(9) ? "" : r.GetString(9);
                string notes = r.IsDBNull(10) ? "" : r.GetString(10);

                long applied = r.IsDBNull(11) ? 0 : r.GetInt64(11);
                long remaining = Math.Max(0, amount - applied);

                dt.Rows.Add(
                    id,
                    clientId,
                    clientName,
                    trabajoId,
                    obra,
                    docNo,
                    MoneyUtil.ToEuros(amount),
                    MoneyUtil.DbToDateTime(paidAtDb),
                    method,
                    reference,
                    notes,
                    MoneyUtil.ToEuros(applied),
                    MoneyUtil.ToEuros(remaining)
                );
            }

            dt.AcceptChanges();
            return dt;
        }

        public void Save(DataTable table)
        {
            using var conn = Data.AppDb.Open();
            using var tx = conn.BeginTransaction();

            // ✅ comandos preparados
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Pagos WHERE id=@id;";
            var del_id = del.Parameters.Add("@id", SqliteType.Integer);

            using var selVentaByDoc = conn.CreateCommand();
            selVentaByDoc.Transaction = tx;
            selVentaByDoc.CommandText =
                "SELECT v.id, v.client_id, COALESCE(t.title,'') " +
                "FROM Ventas v " +
                "LEFT JOIN Trabajos t ON t.id = v.trabajo_id " +
                "WHERE v.doc_no=@d LIMIT 1;";
            var sv_d = selVentaByDoc.Parameters.Add("@d", SqliteType.Text);

            using var selTrabajo = conn.CreateCommand();
            selTrabajo.Transaction = tx;
            selTrabajo.CommandText = "SELECT id FROM Trabajos WHERE client_id=@c AND title=@t LIMIT 1;";
            var st_c = selTrabajo.Parameters.Add("@c", SqliteType.Integer);
            var st_t = selTrabajo.Parameters.Add("@t", SqliteType.Text);

            using var insTrabajo = conn.CreateCommand();
            insTrabajo.Transaction = tx;
            insTrabajo.CommandText = "INSERT INTO Trabajos(client_id, title) VALUES(@c, @t); SELECT last_insert_rowid();";
            var it_c = insTrabajo.Parameters.Add("@c", SqliteType.Integer);
            var it_t = insTrabajo.Parameters.Add("@t", SqliteType.Text);

            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO Pagos(client_id, trabajo_id, doc_no, amount_cents, paid_at, method, reference, notes) " +
                "VALUES(@c, @t, @doc, @a, @d, @m, @r, @n); " +
                "SELECT last_insert_rowid();";

            var p_c = ins.Parameters.Add("@c", SqliteType.Integer);
            var p_t = ins.Parameters.Add("@t", SqliteType.Integer);
            var p_doc = ins.Parameters.Add("@doc", SqliteType.Text);
            var p_a = ins.Parameters.Add("@a", SqliteType.Integer);
            var p_d = ins.Parameters.Add("@d", SqliteType.Text);
            var p_m = ins.Parameters.Add("@m", SqliteType.Text);
            var p_r = ins.Parameters.Add("@r", SqliteType.Text);
            var p_n = ins.Parameters.Add("@n", SqliteType.Text);

            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText =
                "UPDATE Pagos SET client_id=@c, trabajo_id=@t, doc_no=@doc, amount_cents=@a, paid_at=@d, method=@m, reference=@r, notes=@n " +
                "WHERE id=@id;";

            var u_id = upd.Parameters.Add("@id", SqliteType.Integer);
            var u_c = upd.Parameters.Add("@c", SqliteType.Integer);
            var u_t = upd.Parameters.Add("@t", SqliteType.Integer);
            var u_doc = upd.Parameters.Add("@doc", SqliteType.Text);
            var u_a = upd.Parameters.Add("@a", SqliteType.Integer);
            var u_d = upd.Parameters.Add("@d", SqliteType.Text);
            var u_m = upd.Parameters.Add("@m", SqliteType.Text);
            var u_r = upd.Parameters.Add("@r", SqliteType.Text);
            var u_n = upd.Parameters.Add("@n", SqliteType.Text);

            // 1) Deletes
            foreach (DataRow row in table.Rows)
            {
                if (row.RowState != DataRowState.Deleted) continue;

                long id = Convert.ToInt64(row["Id", DataRowVersion.Original] ?? 0);
                if (id <= 0) continue;

                del_id.Value = id;
                del.ExecuteNonQuery();
            }

            // 2) Inserts/Updates
            foreach (DataRow row in table.Rows)
            {
                if (row.RowState == DataRowState.Unchanged || row.RowState == DataRowState.Deleted)
                    continue;

                long id = Convert.ToInt64(row["Id"] ?? 0);
                string docNo = (row["DocNo"]?.ToString() ?? "").Trim();

                long clientId = Convert.ToInt64(row["ClientId"] ?? 0);

                // ✅ Resolver cliente por DocNo SIN abrir otra conexión
                if (clientId <= 0 && !string.IsNullOrWhiteSpace(docNo))
                {
                    if (TryResolveByDocNoTx(selVentaByDoc, docNo, out _, out var resolvedClientId, out var obraTitle))
                    {
                        clientId = resolvedClientId;
                        row["ClientId"] = clientId;

                        if (string.IsNullOrWhiteSpace(row["Obra"]?.ToString()))
                            row["Obra"] = obraTitle;
                    }
                }

                if (clientId <= 0)
                    throw new Exception("Debes seleccionar un Cliente o escribir un DocNo válido (ESV-xxxx) para el pago.");

                string paidAtDb = ToDbDate(row["Fecha"]);

                long amountCents = MoneyUtil.ToCents(Convert.ToDecimal(row["Importe"] ?? 0m));
                if (amountCents <= 0)
                    throw new Exception("El Importe del pago debe ser mayor que 0.");

                string obraTitle2 = (row["Obra"]?.ToString() ?? "").Trim();
                long? trabajoId = null;

                if (!string.IsNullOrWhiteSpace(obraTitle2))
                {
                    trabajoId = GetOrCreateTrabajoTx(selTrabajo, insTrabajo, clientId, obraTitle2);
                }

                string method = (row["Metodo"]?.ToString() ?? "").Trim();
                string reference = (row["Referencia"]?.ToString() ?? "").Trim();
                string notes = (row["Notas"]?.ToString() ?? "").Trim();

                if (id <= 0)
                {
                    p_c.Value = clientId;
                    p_t.Value = (object?)trabajoId ?? DBNull.Value;
                    p_doc.Value = string.IsNullOrWhiteSpace(docNo) ? DBNull.Value : docNo;
                    p_a.Value = amountCents;
                    p_d.Value = paidAtDb;
                    p_m.Value = string.IsNullOrWhiteSpace(method) ? DBNull.Value : method;
                    p_r.Value = string.IsNullOrWhiteSpace(reference) ? DBNull.Value : reference;
                    p_n.Value = string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes;

                    var newId = (long)(ins.ExecuteScalar() ?? 0);
                    row["Id"] = newId;

                    if (!string.IsNullOrWhiteSpace(docNo))
                        AutoApplyByDocNo(conn, tx, newId, docNo, amountCents);
                }
                else
                {
                    u_id.Value = id;
                    u_c.Value = clientId;
                    u_t.Value = (object?)trabajoId ?? DBNull.Value;
                    u_doc.Value = string.IsNullOrWhiteSpace(docNo) ? DBNull.Value : docNo;
                    u_a.Value = amountCents;
                    u_d.Value = paidAtDb;
                    u_m.Value = string.IsNullOrWhiteSpace(method) ? DBNull.Value : method;
                    u_r.Value = string.IsNullOrWhiteSpace(reference) ? DBNull.Value : reference;
                    u_n.Value = string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes;

                    upd.ExecuteNonQuery();

                    if (!string.IsNullOrWhiteSpace(docNo))
                        AutoApplyByDocNo(conn, tx, id, docNo, amountCents);
                }
            }

            tx.Commit();
            table.AcceptChanges();
        }

        private static bool TryResolveByDocNoTx(SqliteCommand selVentaByDoc, string docNo,
            out long ventaId, out long clientId, out string obraTitle)
        {
            ventaId = 0; clientId = 0; obraTitle = "";

            selVentaByDoc.Parameters["@d"].Value = docNo;
            using var r = selVentaByDoc.ExecuteReader();
            if (!r.Read()) return false;

            ventaId = r.GetInt64(0);
            clientId = r.GetInt64(1);
            obraTitle = r.IsDBNull(2) ? "" : r.GetString(2);
            return true;
        }

        private static long GetOrCreateTrabajoTx(SqliteCommand selTrabajo, SqliteCommand insTrabajo, long clientId, string title)
        {
            selTrabajo.Parameters["@c"].Value = clientId;
            selTrabajo.Parameters["@t"].Value = title;
            var existing = selTrabajo.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt64(existing);

            insTrabajo.Parameters["@c"].Value = clientId;
            insTrabajo.Parameters["@t"].Value = title;
            var newId = insTrabajo.ExecuteScalar();
            return Convert.ToInt64(newId ?? 0);
        }

        private static void AutoApplyByDocNo(SqliteConnection conn, SqliteTransaction tx, long paymentId, string docNo, long paymentAmountCents)
        {
            long ventaId = 0;
            long total = 0;
            long paid = 0;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "SELECT v.id, v.total_cents, COALESCE(SUM(pa.amount_cents),0) " +
                    "FROM Ventas v " +
                    "LEFT JOIN PagoAplicaciones pa ON pa.venta_id = v.id " +
                    "WHERE v.doc_no=@d " +
                    "GROUP BY v.id LIMIT 1;";
                cmd.Parameters.AddWithValue("@d", docNo);

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return;

                ventaId = r.GetInt64(0);
                total = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                paid = r.IsDBNull(2) ? 0 : r.GetInt64(2);
            }

            long pending = Math.Max(0, total - paid);
            long apply = Math.Min(paymentAmountCents, pending);
            if (apply <= 0) return;

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM PagoAplicaciones WHERE payment_id=@p;";
                del.Parameters.AddWithValue("@p", paymentId);
                del.ExecuteNonQuery();
            }

            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO PagoAplicaciones(payment_id, venta_id, amount_cents) VALUES(@p,@v,@a);";
                ins.Parameters.AddWithValue("@p", paymentId);
                ins.Parameters.AddWithValue("@v", ventaId);
                ins.Parameters.AddWithValue("@a", apply);
                ins.ExecuteNonQuery();
            }
        }

        private static string ToDbDate(object value)
        {
            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd");

            var s = (value?.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return DateTime.Today.ToString("yyyy-MM-dd");

            if (DateTime.TryParseExact(s, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d1))
                return d1.ToString("yyyy-MM-dd");

            if (DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d2))
                return d2.ToString("yyyy-MM-dd");

            if (DateTime.TryParse(s, out var d3))
                return d3.ToString("yyyy-MM-dd");

            throw new Exception("Fecha inválida para pago. Usa DD/MM/AAAA (ej. 01/02/2026).");
        }

        private static DataTable NewTable()
        {
            var dt = new DataTable("Pagos");

            dt.Columns.Add("Id", typeof(long));
            dt.Columns.Add("ClientId", typeof(long));
            dt.Columns.Add("Cliente", typeof(string));
            dt.Columns.Add("TrabajoId", typeof(long));
            dt.Columns.Add("Obra", typeof(string));

            dt.Columns.Add("DocNo", typeof(string));

            dt.Columns.Add("Importe", typeof(decimal));
            dt.Columns.Add("Fecha", typeof(DateTime));
            dt.Columns.Add("Metodo", typeof(string));
            dt.Columns.Add("Referencia", typeof(string));
            dt.Columns.Add("Notas", typeof(string));

            dt.Columns.Add("Aplicado", typeof(decimal));
            dt.Columns.Add("SinAplicar", typeof(decimal));

            return dt;
        }
    }
}
