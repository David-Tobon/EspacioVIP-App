using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Espacio_VIP_SL_App.Data.Stores
{
    public sealed class PagoAplicacionesStore
    {
        public sealed record VentaPendiente(long VentaId, string DocNo, string Fecha, long TotalCents, long PagadoCents, long PendienteCents);

        public long GetPaymentAmount(long paymentId)
        {
            using var conn = Data.AppDb.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT amount_cents FROM Pagos WHERE id=@id;";
            cmd.Parameters.AddWithValue("@id", paymentId);
            var v = cmd.ExecuteScalar();
            return (v == null || v == DBNull.Value) ? 0 : Convert.ToInt64(v);
        }

        public Dictionary<long, long> GetExistingAllocations(long paymentId)
        {
            var map = new Dictionary<long, long>();

            using var conn = Data.AppDb.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT venta_id, SUM(amount_cents) FROM PagoAplicaciones WHERE payment_id=@p GROUP BY venta_id;";
            cmd.Parameters.AddWithValue("@p", paymentId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long ventaId = r.GetInt64(0);
                long amt = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                map[ventaId] = amt;
            }

            return map;
        }

        public List<VentaPendiente> LoadVentasPendientesForClient(long clientId)
        {
            var list = new List<VentaPendiente>();

            using var conn = Data.AppDb.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT " +
                "v.id, v.doc_no, v.issue_date, v.total_cents, " +
                "COALESCE(SUM(pa.amount_cents),0) AS paid_cents " +
                "FROM Ventas v " +
                "LEFT JOIN PagoAplicaciones pa ON pa.venta_id = v.id " +
                "WHERE v.client_id=@c AND v.status != 'anulado' " +
                "GROUP BY v.id " +
                "HAVING (v.total_cents - COALESCE(SUM(pa.amount_cents),0)) > 0 " +
                "ORDER BY v.issue_date ASC, v.id ASC;";

            cmd.Parameters.AddWithValue("@c", clientId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long id = r.GetInt64(0);
                string docNo = r.IsDBNull(1) ? "" : r.GetString(1);
                string fecha = r.IsDBNull(2) ? "" : r.GetString(2);
                long total = r.IsDBNull(3) ? 0 : r.GetInt64(3);
                long pagado = r.IsDBNull(4) ? 0 : r.GetInt64(4);
                long pendiente = Math.Max(0, total - pagado);

                list.Add(new VentaPendiente(id, docNo, fecha, total, pagado, pendiente));
            }

            return list;
        }

        public void ReplaceAllocations(long paymentId, List<(long ventaId, long amountCents)> allocations)
        {
            using var conn = Data.AppDb.Open();
            using var tx = conn.BeginTransaction();

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM PagoAplicaciones WHERE payment_id=@p;";
                del.Parameters.AddWithValue("@p", paymentId);
                del.ExecuteNonQuery();
            }

            foreach (var (ventaId, amount) in allocations)
            {
                if (ventaId <= 0 || amount <= 0) continue;

                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO PagoAplicaciones(payment_id, venta_id, amount_cents) VALUES(@p, @v, @a);";
                ins.Parameters.AddWithValue("@p", paymentId);
                ins.Parameters.AddWithValue("@v", ventaId);
                ins.Parameters.AddWithValue("@a", amount);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }
}
