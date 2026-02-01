using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Espacio_VIP_SL_App.Data
{
    public static class DbBootstrap
    {
        public static void Initialize()
        {
            using var conn = AppDb.Open();

            Exec(conn, "PRAGMA foreign_keys = ON;");

            EnsureSchemaInfo(conn);

            EnsureClientes(conn);
            EnsureProveedores(conn);

            EnsureTrabajos(conn);
            EnsureVentas(conn);
            EnsurePagos(conn);
            EnsurePagoAplicaciones(conn);

            EnsureIndices(conn);

            SetSchemaVersion(conn, 2);
        }

        // ─────────────────────────────────────────────
        // SchemaInfo
        // ─────────────────────────────────────────────
        private static void EnsureSchemaInfo(SqliteConnection conn)
        {
            Exec(conn,
                "CREATE TABLE IF NOT EXISTS SchemaInfo (" +
                "key TEXT PRIMARY KEY," +
                "value TEXT NOT NULL" +
                ");");

            UpsertSchemaInfo(conn, "schema_version", "2");
            UpsertSchemaInfo(conn, "created_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private static void UpsertSchemaInfo(SqliteConnection conn, string key, string value)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO SchemaInfo(key, value) VALUES(@k, @v) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.ExecuteNonQuery();
        }

        private static void SetSchemaVersion(SqliteConnection conn, int version)
            => UpsertSchemaInfo(conn, "schema_version", version.ToString());

        // ─────────────────────────────────────────────
        // Tablas base
        // ─────────────────────────────────────────────
        private static void EnsureClientes(SqliteConnection conn)
        {
            Exec(conn,
                "CREATE TABLE IF NOT EXISTS Clientes (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "name TEXT NOT NULL," +
                "tax_id TEXT NULL," +
                "phone TEXT NULL," +
                "email TEXT NULL," +
                "address TEXT NULL," +
                "notes TEXT NULL," +
                "created_at TEXT NOT NULL DEFAULT (datetime('now'))," +
                "updated_at TEXT NULL" +
                ");"
            );

            // ✅ FIX: al agregar columnas con ALTER TABLE, NO uses DEFAULT(datetime('now'))
            EnsureColumn(conn, "Clientes", "created_at", "TEXT NULL");
            EnsureColumn(conn, "Clientes", "updated_at", "TEXT NULL");

            // Relleno para registros existentes
            Exec(conn, "UPDATE Clientes SET created_at = COALESCE(created_at, datetime('now')) WHERE created_at IS NULL OR created_at = '';");
        }

        private static void EnsureProveedores(SqliteConnection conn)
        {
            Exec(conn,
                "CREATE TABLE IF NOT EXISTS Proveedores (" +
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
                ");"
            );

            EnsureColumn(conn, "Proveedores", "category", "TEXT NULL");
            EnsureColumn(conn, "Proveedores", "created_at", "TEXT NULL");
            EnsureColumn(conn, "Proveedores", "updated_at", "TEXT NULL");

            Exec(conn, "UPDATE Proveedores SET created_at = COALESCE(created_at, datetime('now')) WHERE created_at IS NULL OR created_at = '';");
        }

        private static void EnsureTrabajos(SqliteConnection conn)
        {
            Exec(conn,
                "CREATE TABLE IF NOT EXISTS Trabajos (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "client_id INTEGER NOT NULL," +
                "title TEXT NOT NULL," +
                "description TEXT NULL," +
                "status TEXT NOT NULL DEFAULT 'abierto'," +
                "total_estimate_cents INTEGER NOT NULL DEFAULT 0," +
                "created_at TEXT NOT NULL DEFAULT (datetime('now'))," +
                "due_at TEXT NULL," +
                "notes TEXT NULL," +
                "FOREIGN KEY(client_id) REFERENCES Clientes(id) ON DELETE RESTRICT" +
                ");"
            );

            EnsureColumn(conn, "Trabajos", "description", "TEXT NULL");
            EnsureColumn(conn, "Trabajos", "total_estimate_cents", "INTEGER NOT NULL DEFAULT 0");
        }

        private static void EnsureVentas(SqliteConnection conn)
        {
            Exec(conn,
                "CREATE TABLE IF NOT EXISTS Ventas (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "doc_no TEXT NOT NULL," +
                "doc_type TEXT NOT NULL DEFAULT 'presupuesto'," +
                "issue_date TEXT NOT NULL," +
                "client_id INTEGER NOT NULL," +
                "trabajo_id INTEGER NULL," +
                "description TEXT NULL," +
                "vat_mode TEXT NOT NULL DEFAULT 'add'," +
                "vat_rate_bp INTEGER NOT NULL DEFAULT 2100," +
                "net_cents INTEGER NOT NULL DEFAULT 0," +
                "vat_cents INTEGER NOT NULL DEFAULT 0," +
                "total_cents INTEGER NOT NULL DEFAULT 0," +
                "status TEXT NOT NULL DEFAULT 'abierto'," +
                "created_at TEXT NOT NULL DEFAULT (datetime('now'))," +
                "FOREIGN KEY(client_id) REFERENCES Clientes(id) ON DELETE RESTRICT," +
                "FOREIGN KEY(trabajo_id) REFERENCES Trabajos(id) ON DELETE SET NULL" +
                ");"
            );

            Exec(conn, "CREATE UNIQUE INDEX IF NOT EXISTS ux_ventas_doc_no ON Ventas(doc_no);");

            EnsureColumn(conn, "Ventas", "description", "TEXT NULL");
            EnsureColumn(conn, "Ventas", "vat_mode", "TEXT NOT NULL DEFAULT 'add'");
            EnsureColumn(conn, "Ventas", "vat_rate_bp", "INTEGER NOT NULL DEFAULT 2100");
        }

        private static void EnsurePagos(SqliteConnection conn)
        {
            Exec(conn,
                "CREATE TABLE IF NOT EXISTS Pagos (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "client_id INTEGER NOT NULL," +
                "trabajo_id INTEGER NULL," +
                "amount_cents INTEGER NOT NULL," +
                "paid_at TEXT NOT NULL," +
                "method TEXT NULL," +
                "reference TEXT NULL," +
                "notes TEXT NULL," +
                "created_at TEXT NOT NULL DEFAULT (datetime('now'))," +
                "FOREIGN KEY(client_id) REFERENCES Clientes(id) ON DELETE RESTRICT," +
                "FOREIGN KEY(trabajo_id) REFERENCES Trabajos(id) ON DELETE SET NULL" +
                ");"
            );

            EnsureColumn(conn, "Pagos", "reference", "TEXT NULL");

            // ✅ FIX (mismo problema): created_at agregado sin default no-constante
            EnsureColumn(conn, "Pagos", "created_at", "TEXT NULL");
            EnsureColumn(conn, "Pagos", "doc_no", "TEXT NULL");

            Exec(conn, "UPDATE Pagos SET created_at = COALESCE(created_at, datetime('now')) WHERE created_at IS NULL OR created_at = '';");

        }

        private static void EnsurePagoAplicaciones(SqliteConnection conn)
        {
            Exec(conn,
                "CREATE TABLE IF NOT EXISTS PagoAplicaciones (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "payment_id INTEGER NOT NULL," +
                "venta_id INTEGER NOT NULL," +
                "amount_cents INTEGER NOT NULL," +
                "FOREIGN KEY(payment_id) REFERENCES Pagos(id) ON DELETE CASCADE," +
                "FOREIGN KEY(venta_id) REFERENCES Ventas(id) ON DELETE CASCADE" +
                ");"
            );
        }

        private static void EnsureIndices(SqliteConnection conn)
        {
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_trabajos_client ON Trabajos(client_id);");

            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_ventas_client ON Ventas(client_id);");
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_ventas_trabajo ON Ventas(trabajo_id);");
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_ventas_issue_date ON Ventas(issue_date);");

            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_pagos_client ON Pagos(client_id);");
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_pagos_trabajo ON Pagos(trabajo_id);");
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_pagos_paid_at ON Pagos(paid_at);");

            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_pagoaplic_payment ON PagoAplicaciones(payment_id);");
            Exec(conn, "CREATE INDEX IF NOT EXISTS idx_pagoaplic_venta ON PagoAplicaciones(venta_id);");
        }

        // ─────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────
        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static void EnsureColumn(SqliteConnection conn, string table, string column, string typeSql)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({table});";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    existing.Add(r.GetString(1)); // name
            }

            if (existing.Contains(column))
                return;

            Exec(conn, $"ALTER TABLE {table} ADD COLUMN {column} {typeSql};");
        }
    }
}
