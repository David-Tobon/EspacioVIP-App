using System;
using System.Data;
using Microsoft.Data.Sqlite;
using Espacio_VIP_SL_App.Utils;

namespace Espacio_VIP_SL_App.Data.Stores
{
    public sealed class VentasStore
    {
        private const int VatRateBpDefault = 2100; // 21%

        public DataTable Load()
        {
            using var conn = Data.AppDb.Open();
            var dt = NewTable();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT " +
                "v.id, v.doc_no, v.doc_type, v.issue_date, v.client_id, c.name, " +
                "v.trabajo_id, COALESCE(t.title,''), v.description, v.status, " +
                "v.vat_mode, v.vat_rate_bp, v.net_cents, v.vat_cents, v.total_cents, " +
                "COALESCE(SUM(pa.amount_cents),0) AS paid_cents " +
                "FROM Ventas v " +
                "JOIN Clientes c ON c.id = v.client_id " +
                "LEFT JOIN Trabajos t ON t.id = v.trabajo_id " +
                "LEFT JOIN PagoAplicaciones pa ON pa.venta_id = v.id " +
                "GROUP BY v.id " +
                "ORDER BY v.issue_date DESC, v.id DESC;";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long id = r.GetInt64(0);
                string docNo = r.IsDBNull(1) ? "" : r.GetString(1);
                string docType = r.IsDBNull(2) ? "presupuesto" : r.GetString(2);

                // ✅ DB string -> DateTime real
                var issueDt = MoneyUtil.DbToDateTime(r.IsDBNull(3) ? "" : r.GetString(3));

                long clientId = r.GetInt64(4);
                string clientName = r.IsDBNull(5) ? "" : r.GetString(5);

                long trabajoId = r.IsDBNull(6) ? 0 : r.GetInt64(6);
                string obra = r.IsDBNull(7) ? "" : r.GetString(7);

                string desc = r.IsDBNull(8) ? "" : r.GetString(8);

                string vatMode = r.IsDBNull(10) ? "add" : r.GetString(10);
                int vatRateBp = r.IsDBNull(11) ? VatRateBpDefault : r.GetInt32(11);

                long net = r.IsDBNull(12) ? 0 : r.GetInt64(12);
                long vat = r.IsDBNull(13) ? 0 : r.GetInt64(13);
                long total = r.IsDBNull(14) ? 0 : r.GetInt64(14);
                long paid = r.IsDBNull(15) ? 0 : r.GetInt64(15);

                long pending = Math.Max(0, total - paid);
                bool conIva = vatMode != "none";

                // ✅ Estado automático (si total>0 y no hay pendiente => cerrado)
                string statusAuto = (total > 0 && pending == 0) ? "cerrado" : "abierto";

                dt.Rows.Add(
                    id,
                    docNo,
                    docType,
                    issueDt,
                    clientId,
                    clientName,
                    trabajoId,
                    obra,
                    desc,
                    statusAuto,
                    conIva,
                    vatRateBp / 100m,
                    MoneyUtil.ToEuros(net),
                    MoneyUtil.ToEuros(vat),
                    MoneyUtil.ToEuros(total),
                    MoneyUtil.ToEuros(paid),
                    MoneyUtil.ToEuros(pending)
                );
            }

            dt.AcceptChanges();
            return dt;
        }

        public string GetNextDocNo()
        {
            using var conn = Data.AppDb.Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText =
                "SELECT MAX(CAST(SUBSTR(doc_no, 5) AS INTEGER)) " +
                "FROM Ventas WHERE doc_no LIKE 'ESV-%';";

            var scalar = cmd.ExecuteScalar();
            int max = 0;
            if (scalar != null && scalar != DBNull.Value)
                max = Convert.ToInt32(scalar);

            int next = max + 1;
            return $"ESV-{next:0000}";
        }

        public void Save(DataTable table)
        {
            using var conn = Data.AppDb.Open();
            using var tx = conn.BeginTransaction();

            // ✅ comandos preparados (reutilizables)
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Ventas WHERE id=@id;";
            var del_id = del.Parameters.Add("@id", SqliteType.Integer);

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
                "INSERT INTO Ventas(doc_no, doc_type, issue_date, client_id, trabajo_id, description, vat_mode, vat_rate_bp, net_cents, vat_cents, total_cents, status) " +
                "VALUES(@doc, @type, @date, @client, @trab, @desc, @vatmode, @vatrate, @net, @vat, @total, @status); " +
                "SELECT last_insert_rowid();";

            var p_doc = ins.Parameters.Add("@doc", SqliteType.Text);
            var p_type = ins.Parameters.Add("@type", SqliteType.Text);
            var p_date = ins.Parameters.Add("@date", SqliteType.Text);
            var p_client = ins.Parameters.Add("@client", SqliteType.Integer);
            var p_trab = ins.Parameters.Add("@trab", SqliteType.Integer);
            var p_desc = ins.Parameters.Add("@desc", SqliteType.Text);
            var p_vatmode = ins.Parameters.Add("@vatmode", SqliteType.Text);
            var p_vatrate = ins.Parameters.Add("@vatrate", SqliteType.Integer);
            var p_net = ins.Parameters.Add("@net", SqliteType.Integer);
            var p_vat = ins.Parameters.Add("@vat", SqliteType.Integer);
            var p_total = ins.Parameters.Add("@total", SqliteType.Integer);
            var p_status = ins.Parameters.Add("@status", SqliteType.Text);

            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText =
                "UPDATE Ventas SET " +
                "doc_no=@doc, doc_type=@type, issue_date=@date, client_id=@client, trabajo_id=@trab, description=@desc, " +
                "vat_mode=@vatmode, vat_rate_bp=@vatrate, net_cents=@net, vat_cents=@vat, total_cents=@total, status=@status " +
                "WHERE id=@id;";

            var u_id = upd.Parameters.Add("@id", SqliteType.Integer);
            var u_doc = upd.Parameters.Add("@doc", SqliteType.Text);
            var u_type = upd.Parameters.Add("@type", SqliteType.Text);
            var u_date = upd.Parameters.Add("@date", SqliteType.Text);
            var u_client = upd.Parameters.Add("@client", SqliteType.Integer);
            var u_trab = upd.Parameters.Add("@trab", SqliteType.Integer);
            var u_desc = upd.Parameters.Add("@desc", SqliteType.Text);
            var u_vatmode = upd.Parameters.Add("@vatmode", SqliteType.Text);
            var u_vatrate = upd.Parameters.Add("@vatrate", SqliteType.Integer);
            var u_net = upd.Parameters.Add("@net", SqliteType.Integer);
            var u_vat = upd.Parameters.Add("@vat", SqliteType.Integer);
            var u_total = upd.Parameters.Add("@total", SqliteType.Integer);
            var u_status = upd.Parameters.Add("@status", SqliteType.Text);

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
                if (string.IsNullOrWhiteSpace(docNo))
                    throw new Exception("DocNo vacío. Ejemplo: ESV-0001");

                string docType = (row["Tipo"]?.ToString() ?? "presupuesto").Trim();

                // ✅ Fecha: acepta DateTime o string
                string issueDateDb = ToDbDate(row["Fecha"]);

                long clientId = Convert.ToInt64(row["ClientId"] ?? 0);
                if (clientId <= 0)
                    throw new Exception("Debes seleccionar un Cliente para la venta.");

                // ✅ Trabajo/Obra dentro de la MISMA transacción
                string obraTitle = (row["Obra"]?.ToString() ?? "").Trim();
                long? trabajoId = null;
                if (!string.IsNullOrWhiteSpace(obraTitle))
                {
                    trabajoId = GetOrCreateTrabajoTx(conn, tx, selTrabajo, insTrabajo, clientId, obraTitle);
                }

                bool conIva = row["ConIVA"] != DBNull.Value && Convert.ToBoolean(row["ConIVA"]);

                decimal netEur = Convert.ToDecimal(row["Neto"] ?? 0m);
                long netCents = MoneyUtil.ToCents(netEur);

                long vatCents = 0;
                long totalCents = netCents;

                if (conIva)
                {
                    vatCents = (long)Math.Round(netCents * 0.21m, 0, MidpointRounding.AwayFromZero);
                    totalCents = netCents + vatCents;
                }

                string vatMode = conIva ? "add" : "none";
                int vatRateBp = conIva ? VatRateBpDefault : 0;

                string desc = (row["Descripcion"]?.ToString() ?? "").Trim();

                // ✅ Estado automático según Total vs Pagado (si existe en tabla)
                long paidCents = 0;
                try { paidCents = MoneyUtil.ToCents(Convert.ToDecimal(row["Pagado"] ?? 0m)); } catch { paidCents = 0; }
                string statusAuto = (totalCents > 0 && paidCents >= totalCents) ? "cerrado" : "abierto";

                if (id <= 0)
                {
                    p_doc.Value = docNo;
                    p_type.Value = docType;
                    p_date.Value = issueDateDb;
                    p_client.Value = clientId;
                    p_trab.Value = (object?)trabajoId ?? DBNull.Value;
                    p_desc.Value = string.IsNullOrWhiteSpace(desc) ? DBNull.Value : desc;
                    p_vatmode.Value = vatMode;
                    p_vatrate.Value = vatRateBp;
                    p_net.Value = netCents;
                    p_vat.Value = vatCents;
                    p_total.Value = totalCents;
                    p_status.Value = statusAuto;

                    var newId = (long)(ins.ExecuteScalar() ?? 0);
                    row["Id"] = newId;
                }
                else
                {
                    u_id.Value = id;
                    u_doc.Value = docNo;
                    u_type.Value = docType;
                    u_date.Value = issueDateDb;
                    u_client.Value = clientId;
                    u_trab.Value = (object?)trabajoId ?? DBNull.Value;
                    u_desc.Value = string.IsNullOrWhiteSpace(desc) ? DBNull.Value : desc;
                    u_vatmode.Value = vatMode;
                    u_vatrate.Value = vatRateBp;
                    u_net.Value = netCents;
                    u_vat.Value = vatCents;
                    u_total.Value = totalCents;
                    u_status.Value = statusAuto;

                    upd.ExecuteNonQuery();
                }

                row["IVA"] = MoneyUtil.ToEuros(vatCents);
                row["Total"] = MoneyUtil.ToEuros(totalCents);
                row["Estado"] = statusAuto;
            }

            tx.Commit();
            table.AcceptChanges();
        }

        private static long GetOrCreateTrabajoTx(
            SqliteConnection conn,
            SqliteTransaction tx,
            SqliteCommand selTrabajo,
            SqliteCommand insTrabajo,
            long clientId,
            string title)
        {
            // SELECT
            selTrabajo.Transaction = tx;
            selTrabajo.Parameters["@c"].Value = clientId;
            selTrabajo.Parameters["@t"].Value = title;

            var existing = selTrabajo.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt64(existing);

            // INSERT
            insTrabajo.Transaction = tx;
            insTrabajo.Parameters["@c"].Value = clientId;
            insTrabajo.Parameters["@t"].Value = title;

            var newId = insTrabajo.ExecuteScalar();
            return Convert.ToInt64(newId ?? 0);
        }

        private static string ToDbDate(object value)
        {
            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd");

            var s = (value?.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
                return DateTime.Today.ToString("yyyy-MM-dd");

            // acepta "dd/MM/yyyy" o "yyyy-MM-dd"
            if (DateTime.TryParseExact(s, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d1))
                return d1.ToString("yyyy-MM-dd");

            if (DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d2))
                return d2.ToString("yyyy-MM-dd");

            if (DateTime.TryParse(s, out var d3))
                return d3.ToString("yyyy-MM-dd");

            throw new Exception("Fecha inválida. Usa DD/MM/AAAA (ej. 01/02/2026).");
        }

        private static DataTable NewTable()
        {
            var dt = new DataTable("Ventas");

            dt.Columns.Add("Id", typeof(long));
            dt.Columns.Add("DocNo", typeof(string));
            dt.Columns.Add("Tipo", typeof(string));
            dt.Columns.Add("Fecha", typeof(DateTime));

            dt.Columns.Add("ClientId", typeof(long));
            dt.Columns.Add("Cliente", typeof(string));

            dt.Columns.Add("TrabajoId", typeof(long));
            dt.Columns.Add("Obra", typeof(string));

            dt.Columns.Add("Descripcion", typeof(string));
            dt.Columns.Add("Estado", typeof(string));

            dt.Columns.Add("ConIVA", typeof(bool));
            dt.Columns.Add("IVA%", typeof(decimal)); // solo display
            dt.Columns.Add("Neto", typeof(decimal));
            dt.Columns.Add("IVA", typeof(decimal));
            dt.Columns.Add("Total", typeof(decimal));

            dt.Columns.Add("Pagado", typeof(decimal));
            dt.Columns.Add("Pendiente", typeof(decimal));

            return dt;
        }
    }
}
