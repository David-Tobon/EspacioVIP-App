using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Espacio_VIP_SL_App.Data
{
    public static class AppDb
    {
        private static readonly string _folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EspacioVIP");

        private static readonly string _dbPath = Path.Combine(_folder, "espacio_vip.db");

        public static string DbPath => _dbPath;

        public static SqliteConnection Open()
        {
            Directory.CreateDirectory(_folder);

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = 10 // segundos
                // ⚠️ NO Cache=Shared
            };

            var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "PRAGMA foreign_keys = ON;" +
                    "PRAGMA journal_mode = WAL;" +
                    "PRAGMA synchronous = NORMAL;" +
                    "PRAGMA temp_store = MEMORY;" +
                    "PRAGMA busy_timeout = 8000;";
                cmd.ExecuteNonQuery();
            }

            return conn;
        }
    }
}
