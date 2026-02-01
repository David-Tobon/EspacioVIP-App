using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Espacio_VIP_SL_App.Data.Stores;
using Espacio_VIP_SL_App.Utils;

namespace Espacio_VIP_SL_App
{
    public class ApplyPaymentForm : Form
    {
        private readonly long _paymentId;
        private readonly long _clientId;
        private readonly PagoAplicacionesStore _store;

        private DataGridView _grid;
        private Label _lblInfo;
        private Button _btnOk;
        private Button _btnCancel;

        private List<PagoAplicacionesStore.VentaPendiente> _ventas = new();
        private Dictionary<long, long> _existing = new();

        public ApplyPaymentForm(long paymentId, long clientId, PagoAplicacionesStore store)
        {
            _paymentId = paymentId;
            _clientId = clientId;
            _store = store;

            Text = "Aplicar Pago a Ventas";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(860, 520);
            MinimumSize = new Size(860, 520);

            BuildUI();
            LoadData();
        }

        private void BuildUI()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(12, 10, 12, 10) };
            _lblInfo = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
            top.Controls.Add(_lblInfo);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12, 10, 12, 10) };
            var bl = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };

            _btnOk = new Button { Text = "Aplicar", Width = 120, Height = 34, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            _btnCancel = new Button { Text = "Cancelar", Width = 120, Height = 34, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };

            _btnOk.Click += (_, __) => OnApply();
            _btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

            bl.Controls.Add(_btnOk);
            bl.Controls.Add(_btnCancel);
            bottom.Controls.Add(bl);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoGenerateColumns = false
            };

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(top);
        }

        private void LoadData()
        {
            long paymentAmount = _store.GetPaymentAmount(_paymentId);
            _existing = _store.GetExistingAllocations(_paymentId);
            _ventas = _store.LoadVentasPendientesForClient(_clientId);

            long alreadyApplied = _existing.Values.Sum();
            long remaining = Math.Max(0, paymentAmount - alreadyApplied);

            _lblInfo.Text = $"Pago #{_paymentId} | Total: {MoneyUtil.EurosString(paymentAmount)} € | Ya aplicado: {MoneyUtil.EurosString(alreadyApplied)} € | Disponible: {MoneyUtil.EurosString(remaining)} €";

            BuildGrid(paymentAmount);
        }

        private void BuildGrid(long paymentAmount)
        {
            _grid.Columns.Clear();

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "VentaId", DataPropertyName = "VentaId", Visible = false });

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "DocNo", DataPropertyName = "DocNo", Width = 110, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha", DataPropertyName = "Fecha", Width = 95, ReadOnly = true });

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Total (€)", DataPropertyName = "Total", Width = 110, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pagado (€)", DataPropertyName = "Pagado", Width = 110, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pendiente (€)", DataPropertyName = "Pendiente", Width = 110, ReadOnly = true });

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Aplicar (€)", DataPropertyName = "Aplicar", Width = 120 });

            var table = new System.Data.DataTable();
            table.Columns.Add("VentaId", typeof(long));
            table.Columns.Add("DocNo", typeof(string));
            table.Columns.Add("Fecha", typeof(string));
            table.Columns.Add("Total", typeof(decimal));
            table.Columns.Add("Pagado", typeof(decimal));
            table.Columns.Add("Pendiente", typeof(decimal));
            table.Columns.Add("Aplicar", typeof(decimal));

            foreach (var v in _ventas)
            {
                var applyCents = _existing.TryGetValue(v.VentaId, out var amt) ? amt : 0;
                table.Rows.Add(
                    v.VentaId,
                    v.DocNo,
                    v.Fecha,
                    MoneyUtil.ToEuros(v.TotalCents),
                    MoneyUtil.ToEuros(v.PagadoCents),
                    MoneyUtil.ToEuros(v.PendienteCents),
                    MoneyUtil.ToEuros(applyCents)
                );
            }

            _grid.DataSource = table;
        }

        private void OnApply()
        {
            var dt = _grid.DataSource as System.Data.DataTable;
            if (dt == null) return;

            long paymentAmount = _store.GetPaymentAmount(_paymentId);

            // Validar y armar allocations
            var allocations = new List<(long ventaId, long amountCents)>();
            long sum = 0;

            foreach (System.Data.DataRow row in dt.Rows)
            {
                long ventaId = Convert.ToInt64(row["VentaId"]);
                long pendienteCents = MoneyUtil.ToCents(row["Pendiente"]);
                long aplicarCents = MoneyUtil.ToCents(row["Aplicar"]);

                if (aplicarCents <= 0) continue;

                if (aplicarCents > pendienteCents)
                {
                    MessageBox.Show("No puedes aplicar más de lo pendiente en una venta.", Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                allocations.Add((ventaId, aplicarCents));
                sum += aplicarCents;
            }

            if (sum > paymentAmount)
            {
                MessageBox.Show("La suma aplicada excede el total del pago.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _store.ReplaceAllocations(_paymentId, allocations);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
