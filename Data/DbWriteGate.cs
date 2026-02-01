using System;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace Espacio_VIP_SL_App.Data
{
    public static class DbWriteGate
    {
        private static readonly SemaphoreSlim _sem = new(1, 1);

        public static void RunWithRetry(Action action, int maxRetries = 6)
        {
            _sem.Wait();
            try
            {
                int delayMs = 60;

                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        action();
                        return;
                    }
                    catch (SqliteException ex) when ((ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6) && attempt < maxRetries)
                    {
                        Thread.Sleep(delayMs);
                        delayMs = Math.Min(delayMs * 2, 800);
                    }
                }
            }
            finally
            {
                _sem.Release();
            }
        }
    }
}
