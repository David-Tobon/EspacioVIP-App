using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Espacio_VIP_SL_App.Data.Stores
{
    public sealed class LookupsStore
    {
        public sealed record Lookup(long Id, string Name);

        public List<Lookup> LoadClientsWithAll()
        {
            var list = new List<Lookup>
            {
                new Lookup(0, "(Todos)")
            };

            using var conn = Data.AppDb.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM Clientes ORDER BY name;";

            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new Lookup(r.GetInt64(0), r.IsDBNull(1) ? "" : r.GetString(1)));

            return list;
        }

        public long GetOrCreateTrabajo(long clientId, string title)
        {
            title = (title ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title)) return 0;

            using var conn = Data.AppDb.Open();

            using (var find = conn.CreateCommand())
            {
                find.CommandText = "SELECT id FROM Trabajos WHERE client_id=@c AND lower(title)=lower(@t) LIMIT 1;";
                find.Parameters.AddWithValue("@c", clientId);
                find.Parameters.AddWithValue("@t", title);

                var existing = find.ExecuteScalar();
                if (existing != null && existing != System.DBNull.Value)
                    return (long)existing;
            }

            using (var ins = conn.CreateCommand())
            {
                ins.CommandText =
                    "INSERT INTO Trabajos(client_id, title, status, total_estimate_cents) " +
                    "VALUES(@c, @t, 'abierto', 0); " +
                    "SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("@c", clientId);
                ins.Parameters.AddWithValue("@t", title);

                return (long)(ins.ExecuteScalar() ?? 0);
            }
        }

        public bool TryGetVentaByDocNo(string docNo, out long ventaId, out long clientId, out string obraTitle)
        {
            ventaId = 0;
            clientId = 0;
            obraTitle = "";

            docNo = (docNo ?? "").Trim();
            if (string.IsNullOrWhiteSpace(docNo)) return false;

            using var conn = Data.AppDb.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT v.id, v.client_id, COALESCE(t.title,'') " +
                "FROM Ventas v " +
                "LEFT JOIN Trabajos t ON t.id = v.trabajo_id " +
                "WHERE v.doc_no=@d LIMIT 1;";
            cmd.Parameters.AddWithValue("@d", docNo);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return false;

            ventaId = r.GetInt64(0);
            clientId = r.GetInt64(1);
            obraTitle = r.IsDBNull(2) ? "" : r.GetString(2);
            return true;
        }
    }
}
