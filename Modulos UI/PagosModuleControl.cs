using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Espacio_VIP_SL_App.Data;
using Espacio_VIP_SL_App.Data.Stores;

namespace Espacio_VIP_SL_App
{
    public class PagosModuleControl : UserControl, IModuleSaveable
    {
        private readonly PagosStore _store = new();
        private readonly LookupsStore _lookups = new();
        private readonly PagoAplicacionesStore _apStore = new();

        private DataTable _table;
        private BindingSource _bs;

        private ComboBox _cbClientQuick;
        private TextBox _txtSearch;
        private Button _btnNew;
        private Button _btnSave;
        private Button _btnRefresh;
        private Button _btnApply;
        private Button _btnDelete;

        private DataGridView _grid;

        public string ModuleTitle => "Registro Pagos";
        public bool HasUnsavedChanges => _table?.GetChanges() != null;

        public PagosModuleControl()
        {
            BackColor = Color.White;
            BuildUI();
            LoadData();
        }

        private void BuildUI()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12, 10, 12, 10), BackColor = Color.White };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                BackColor = Color.White
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); // cliente
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // buscar
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // nuevo
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // guardar
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // refrescar
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); // aplicar
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // eliminar

            _cbClientQuick = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _txtSearch = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Buscar (método, referencia, docNo, obra…)" };

            _btnNew = new Button { Dock = DockStyle.Fill, Text = "Nuevo", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _btnSave = new Button { Dock = DockStyle.Fill, Text = "Guardar", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _btnRefresh = new Button { Dock = DockStyle.Fill, Text = "Refrescar", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _btnApply = new Button { Dock = DockStyle.Fill, Text = "Aplicar a Ventas", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _btnDelete = new Button { Dock = DockStyle.Fill, Text = "Eliminar", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };

            _btnNew.Click += (_, __) => AddNew();
            _btnSave.Click += (_, __) => SaveWithFeedback();
            _btnRefresh.Click += (_, __) => LoadData();
            _btnApply.Click += (_, __) => OpenApplyDialog();
            _btnDelete.Click += (_, __) => DeleteSelected();

            _txtSearch.TextChanged += (_, __) => ApplyFilterSafe();
            _cbClientQuick.SelectionChangeCommitted += (_, __) => ApplyFilterSafe();

            layout.Controls.Add(_cbClientQuick, 0, 0);
            layout.Controls.Add(_txtSearch, 1, 0);
            layout.Controls.Add(_btnNew, 2, 0);
            layout.Controls.Add(_btnSave, 3, 0);
            layout.Controls.Add(_btnRefresh, 4, 0);
            layout.Controls.Add(_btnApply, 5, 0);
            layout.Controls.Add(_btnDelete, 6, 0);

            top.Controls.Add(layout);

            var sep = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Color.FromArgb(235, 235, 235) };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                RowHeadersVisible = false
            };

            // ✅ FIX “se borra letra por letra”
            _grid.CurrentCellDirtyStateChanged += (_, __) =>
            {
                if (!_grid.IsCurrentCellDirty) return;
                if (_grid.CurrentCell == null) return;

                var col = _grid.Columns[_grid.CurrentCell.ColumnIndex];
                if (col is DataGridViewComboBoxColumn || col is DataGridViewCheckBoxColumn)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            _grid.CellParsing += (s, e) =>
            {
                var dp = _grid.Columns[e.ColumnIndex].DataPropertyName;
                if (dp == "Importe" && e.Value is string str && Utils.MoneyUtil.TryParseDecimalFlexible(str, out var dec))
                {
                    e.Value = dec;
                    e.ParsingApplied = true;
                }
            };

            _grid.CellFormatting += (s, e) =>
            {
                var dp = _grid.Columns[e.ColumnIndex].DataPropertyName;

                if ((dp == "Importe" || dp == "Aplicado" || dp == "SinAplicar") && e.Value is decimal d)
                {
                    e.Value = d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    e.FormattingApplied = true;
                }

                if (dp == "Fecha" && e.Value is DateTime dt)
                {
                    e.Value = dt.ToString("dd/MM/yyyy");
                    e.FormattingApplied = true;
                }
            };

            _grid.DataError += (_, __) => { };

            Controls.Add(_grid);
            Controls.Add(sep);
            Controls.Add(top);
        }

        private bool _applyingFilter;

        private void LoadData()
        {
            var clients = _lookups.LoadClientsWithAll();
            _cbClientQuick.DataSource = clients;
            _cbClientQuick.DisplayMember = "Name";
            _cbClientQuick.ValueMember = "Id";
            if (_cbClientQuick.Items.Count > 0) _cbClientQuick.SelectedIndex = 0;

            _table = _store.Load();
            _bs = new BindingSource { DataSource = _table };
            _grid.DataSource = _bs;

            BuildColumns(clients);
            ApplyFilterSafe();
        }

        private void BuildColumns(List<LookupsStore.Lookup> clients)
        {
            _grid.Columns.Clear();

            var clientsRow = clients.Select(c => c.Id == 0 ? new LookupsStore.Lookup(0, "(Sin cliente)") : c).ToList();

            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", Visible = false });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "DocNo", DataPropertyName = "DocNo", Width = 110 });

            var clientCol = new DataGridViewComboBoxColumn
            {
                HeaderText = "Cliente",
                DataPropertyName = "ClientId",
                Width = 220,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DataSource = clientsRow,
                ValueMember = "Id",
                DisplayMember = "Name"
            };
            _grid.Columns.Add(clientCol);

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Obra", DataPropertyName = "Obra", Width = 160 });

            var colFecha = new DataGridViewTextBoxColumn { HeaderText = "Fecha", DataPropertyName = "Fecha", Width = 110 };
            colFecha.DefaultCellStyle.Format = "dd/MM/yyyy";
            _grid.Columns.Add(colFecha);

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Importe",
                HeaderText = "Importe (€)",
                DataPropertyName = "Importe",
                Width = 95
            });

            var metodoCol = new DataGridViewComboBoxColumn
            {
                HeaderText = "Método",
                DataPropertyName = "Metodo",
                Width = 140,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DataSource = new[] { "Efectivo", "Transferencia" }
            };
            _grid.Columns.Add(metodoCol);

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Referencia", DataPropertyName = "Referencia", Width = 140 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Notas", DataPropertyName = "Notas", Width = 220 });

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Aplicado (€)", DataPropertyName = "Aplicado", ReadOnly = true, Width = 95 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sin aplicar (€)", DataPropertyName = "SinAplicar", ReadOnly = true, Width = 100 });
        }

        private void ApplyFilterSafe()
        {
            if (_bs == null) return;
            if (_applyingFilter) return;

            _applyingFilter = true;
            try
            {
                _grid.EndEdit();
                _bs.EndEdit();

                var text = (_txtSearch.Text ?? "").Trim();

                long selectedClientId = 0;
                if (_cbClientQuick.SelectedItem is LookupsStore.Lookup l)
                    selectedClientId = l.Id;

                var filters = new List<string>();

                if (selectedClientId > 0)
                    filters.Add($"ClientId = {selectedClientId}");

                if (!string.IsNullOrWhiteSpace(text))
                {
                    var safe = text.Replace("'", "''");
                    filters.Add($"(Metodo LIKE '%{safe}%' OR Referencia LIKE '%{safe}%' OR Notas LIKE '%{safe}%' OR Obra LIKE '%{safe}%' OR DocNo LIKE '%{safe}%')");
                }

                _grid.CurrentCell = null;
                _bs.Filter = filters.Count == 0 ? "" : string.Join(" AND ", filters);

                if (_grid.Rows.Count > 0)
                {
                    var firstVisibleCol = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
                    if (firstVisibleCol != null)
                        _grid.CurrentCell = _grid.Rows[0].Cells[firstVisibleCol.Index];
                }
            }
            finally
            {
                _applyingFilter = false;
            }
        }

        private void AddNew()
        {
            var sel = _cbClientQuick.SelectedItem as LookupsStore.Lookup;

            var r = _table.NewRow();
            r["Id"] = 0L;
            r["DocNo"] = "";
            r["ClientId"] = (sel != null && sel.Id > 0) ? sel.Id : 0L;
            r["Cliente"] = (sel != null && sel.Id > 0) ? sel.Name : "";
            r["Obra"] = "";
            r["Fecha"] = DateTime.Today;
            r["Importe"] = 0m;
            r["Metodo"] = "Efectivo";
            r["Referencia"] = "";
            r["Notas"] = "";
            r["Aplicado"] = 0m;
            r["SinAplicar"] = 0m;

            _table.Rows.Add(r);

            _grid.ClearSelection();
            int idx = _grid.Rows.Count - 1;
            if (idx >= 0)
            {
                _grid.Rows[idx].Selected = true;

                int idxImp = 0;
                foreach (DataGridViewColumn c in _grid.Columns)
                {
                    if (c.DataPropertyName == "Importe") { idxImp = c.Index; break; }
                }

                _grid.CurrentCell = _grid.Rows[idx].Cells[idxImp];
                _grid.BeginEdit(true);
            }
        }

        private void OpenApplyDialog()
        {
            if (_grid.SelectedRows.Count != 1)
            {
                MessageBox.Show("Selecciona 1 pago para aplicar.", ModuleTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var gRow = _grid.SelectedRows[0];
            if (gRow.DataBoundItem is not DataRowView drv) return;

            long paymentId = Convert.ToInt64(drv.Row["Id"] ?? 0);
            if (paymentId <= 0)
            {
                var ok = SaveWithFeedback();
                if (!ok) return;
                paymentId = Convert.ToInt64(drv.Row["Id"] ?? 0);
                if (paymentId <= 0) return;
            }

            long clientId = Convert.ToInt64(drv.Row["ClientId"] ?? 0);
            if (clientId <= 0)
            {
                MessageBox.Show("Para aplicar manualmente, el pago debe tener un cliente válido (o usa DocNo y guarda).",
                    ModuleTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var form = new ApplyPaymentForm(paymentId, clientId, _apStore);
            var res = form.ShowDialog();

            if (res == DialogResult.OK)
                LoadData();
        }

        private void DeleteSelected()
        {
            if (_grid.SelectedRows.Count == 0) return;

            var r = MessageBox.Show("¿Eliminar pagos seleccionados?",
                ModuleTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (r != DialogResult.Yes) return;

            foreach (DataGridViewRow gRow in _grid.SelectedRows)
            {
                if (gRow.DataBoundItem is DataRowView drv)
                    drv.Row.Delete();
            }
        }

        public void SaveSilent() => SaveInternal(showFeedback: false);
        public bool SaveWithFeedback() => SaveInternal(showFeedback: true);

        private bool SaveInternal(bool showFeedback)
        {
            try
            {
                if (_table == null) return true;
                if (_table.GetChanges() == null) return true;

                Cursor.Current = Cursors.WaitCursor;

                this.Validate();
                _grid.EndEdit();
                _bs.EndEdit();

                DbWriteGate.RunWithRetry(() => _store.Save(_table));

                if (showFeedback)
                {
                    _table = _store.Load();
                    _bs.DataSource = _table;
                    ApplyFilterSafe();

                    MessageBox.Show("Pagos guardados ✅", ModuleTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (showFeedback)
                    MessageBox.Show("Error guardando pagos ❌\n\n" + ex.Message, ModuleTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }
    }
}
