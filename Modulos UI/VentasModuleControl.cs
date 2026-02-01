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
    public class VentasModuleControl : UserControl, IModuleSaveable
    {
        private readonly VentasStore _store = new();
        private readonly LookupsStore _lookups = new();

        private DataTable _table;
        private BindingSource _bs;

        private ComboBox _cbClientQuick;
        private TextBox _txtSearch;
        private Button _btnNewPresu;
        private Button _btnNewFactura;
        private Button _btnSave;
        private Button _btnRefresh;
        private Button _btnDelete;

        private DataGridView _grid;

        public string ModuleTitle => "Facturas Venta";
        public bool HasUnsavedChanges => _table?.GetChanges() != null;

        public VentasModuleControl()
        {
            BackColor = Color.White;
            BuildUI();
            LoadData();
        }

        private void BuildUI()
        {
            var top = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                Padding = new Padding(12, 10, 12, 10),
                BackColor = Color.White
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                BackColor = Color.White
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); // cliente
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // buscar
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); // nuevo presu
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); // nueva factura
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // guardar
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // refrescar
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // eliminar

            _cbClientQuick = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _txtSearch = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Buscar (DocNo, descripción, obra…)" };

            _btnNewPresu = new Button { Dock = DockStyle.Fill, Text = "Nuevo Presupuesto", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _btnNewFactura = new Button { Dock = DockStyle.Fill, Text = "Nueva Factura", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _btnSave = new Button { Dock = DockStyle.Fill, Text = "Guardar", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _btnRefresh = new Button { Dock = DockStyle.Fill, Text = "Refrescar", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            _btnDelete = new Button { Dock = DockStyle.Fill, Text = "Eliminar", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };

            _btnNewPresu.Click += (_, __) => AddNew("presupuesto");
            _btnNewFactura.Click += (_, __) => AddNew("factura");
            _btnSave.Click += (_, __) => SaveWithFeedback();
            _btnRefresh.Click += (_, __) => LoadData();
            _btnDelete.Click += (_, __) => DeleteSelected();

            _txtSearch.TextChanged += (_, __) => ApplyFilter();
            _cbClientQuick.SelectionChangeCommitted += (_, __) => ApplyFilter();

            layout.Controls.Add(_cbClientQuick, 0, 0);
            layout.Controls.Add(_txtSearch, 1, 0);
            layout.Controls.Add(_btnNewPresu, 2, 0);
            layout.Controls.Add(_btnNewFactura, 3, 0);
            layout.Controls.Add(_btnSave, 4, 0);
            layout.Controls.Add(_btnRefresh, 5, 0);
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

            _grid.CellParsing += Grid_CellParsing;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.DataError += (_, __) => { };

            _grid.CellValueChanged += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                var col = _grid.Columns[e.ColumnIndex].DataPropertyName;

                if (col == "Neto" || col == "ConIVA")
                    RecalcRow(e.RowIndex);
            };

            Controls.Add(_grid);
            Controls.Add(sep);
            Controls.Add(top);
        }

        private void Grid_CellParsing(object? sender, DataGridViewCellParsingEventArgs e)
        {
            var dp = _grid.Columns[e.ColumnIndex].DataPropertyName;

            if (dp == "Neto" && e.Value is string s)
            {
                if (Utils.MoneyUtil.TryParseDecimalFlexible(s, out var dec))
                {
                    e.Value = dec;
                    e.ParsingApplied = true;
                }
            }
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            var dp = _grid.Columns[e.ColumnIndex].DataPropertyName;

            if ((dp == "Neto" || dp == "IVA" || dp == "Total" || dp == "Pagado" || dp == "Pendiente") && e.Value is decimal d)
            {
                e.Value = d.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                e.FormattingApplied = true;
            }

            if (dp == "Fecha" && e.Value is DateTime dt)
            {
                e.Value = dt.ToString("dd/MM/yyyy");
                e.FormattingApplied = true;
            }
        }

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
            ApplyFilter();
        }

        private void BuildColumns(List<LookupsStore.Lookup> clients)
        {
            _grid.Columns.Clear();

            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", Visible = false });

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Nº",
                DataPropertyName = "DocNo",
                Width = 110,
                ReadOnly = false
            });

            var tipoCol = new DataGridViewComboBoxColumn
            {
                HeaderText = "Tipo",
                DataPropertyName = "Tipo",
                Width = 110,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DataSource = new[]
                {
                    new KeyValuePair<string,string>("presupuesto","Presupuesto"),
                    new KeyValuePair<string,string>("factura","Factura"),
                },
                ValueMember = "Key",
                DisplayMember = "Value"
            };
            _grid.Columns.Add(tipoCol);

            var colFecha = new DataGridViewTextBoxColumn { HeaderText = "Fecha", DataPropertyName = "Fecha", Width = 110 };
            colFecha.DefaultCellStyle.Format = "dd/MM/yyyy";
            _grid.Columns.Add(colFecha);

            var clientCol = new DataGridViewComboBoxColumn
            {
                HeaderText = "Cliente",
                DataPropertyName = "ClientId",
                Width = 220,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DataSource = clients.Where(c => c.Id != 0).ToList(),
                ValueMember = "Id",
                DisplayMember = "Name"
            };
            _grid.Columns.Add(clientCol);

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Obra", DataPropertyName = "Obra", Width = 160 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción", DataPropertyName = "Descripcion", Width = 240 });

            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Con IVA", DataPropertyName = "ConIVA", Width = 70 });

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Neto", HeaderText = "Neto (€)", DataPropertyName = "Neto", Width = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "IVA", HeaderText = "IVA (€)", DataPropertyName = "IVA", ReadOnly = true, Width = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Total (€)", DataPropertyName = "Total", ReadOnly = true, Width = 90 });

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pagado (€)", DataPropertyName = "Pagado", ReadOnly = true, Width = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pendiente (€)", DataPropertyName = "Pendiente", ReadOnly = true, Width = 100 });
        }

        private void ApplyFilter()
        {
            if (_bs == null) return;

            var text = (_txtSearch.Text ?? "").Trim();
            long selectedClientId = 0;
            if (_cbClientQuick.SelectedItem is LookupsStore.Lookup l) selectedClientId = l.Id;

            var filters = new List<string>();

            if (selectedClientId > 0)
                filters.Add($"ClientId = {selectedClientId}");

            if (!string.IsNullOrWhiteSpace(text))
            {
                var safe = text.Replace("'", "''");
                filters.Add($"(DocNo LIKE '%{safe}%' OR Descripcion LIKE '%{safe}%' OR Obra LIKE '%{safe}%')");
            }

            _bs.Filter = filters.Count == 0 ? "" : string.Join(" AND ", filters);
        }

        private void RecalcRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            if (_grid.Rows[rowIndex].DataBoundItem is not DataRowView rowView) return;

            var row = rowView.Row;

            decimal net = 0m;
            try { net = Convert.ToDecimal(row["Neto"] ?? 0m); } catch { net = 0m; }

            bool conIva = row["ConIVA"] != DBNull.Value && Convert.ToBoolean(row["ConIVA"]);
            decimal iva = conIva ? Math.Round(net * 0.21m, 2, MidpointRounding.AwayFromZero) : 0m;
            decimal total = Math.Round(net + iva, 2, MidpointRounding.AwayFromZero);

            row["IVA"] = iva;
            row["Total"] = total;
        }

        private void AddNew(string docType)
        {
            LookupsStore.Lookup client = null;

            if (_cbClientQuick.SelectedItem is LookupsStore.Lookup sel && sel.Id > 0)
                client = sel;

            if (client == null)
            {
                if (_cbClientQuick.Items.Count > 1 && _cbClientQuick.Items[1] is LookupsStore.Lookup l2)
                    client = l2;
            }

            if (client == null || client.Id <= 0)
            {
                MessageBox.Show("Primero crea al menos 1 cliente en Database → Clientes.", ModuleTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var docNo = _store.GetNextDocNo();

            var r = _table.NewRow();
            r["Id"] = 0L;
            r["DocNo"] = docNo;
            r["Tipo"] = docType;
            r["Fecha"] = DateTime.Today;
            r["ClientId"] = client.Id;
            r["Cliente"] = client.Name;
            r["Obra"] = "";
            r["Descripcion"] = "";
            r["ConIVA"] = true;
            r["Neto"] = 0m;
            r["IVA"] = 0m;
            r["Total"] = 0m;
            r["Pagado"] = 0m;
            r["Pendiente"] = 0m;

            _table.Rows.Add(r);

            _grid.ClearSelection();
            int idx = _grid.Rows.Count - 1;
            if (idx >= 0)
            {
                _grid.Rows[idx].Selected = true;

                int idxNeto = 0;
                foreach (DataGridViewColumn c in _grid.Columns)
                {
                    if (c.DataPropertyName == "Neto") { idxNeto = c.Index; break; }
                }

                _grid.CurrentCell = _grid.Rows[idx].Cells[idxNeto];
                _grid.BeginEdit(true);
            }
        }

        private void DeleteSelected()
        {
            if (_grid.SelectedRows.Count == 0) return;

            var r = MessageBox.Show("¿Eliminar ventas seleccionadas?",
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
                if (_table.GetChanges() == null) return true; // ✅ no tocar DB si no hay cambios

                Cursor.Current = Cursors.WaitCursor;

                this.Validate();
                _grid.EndEdit();
                _bs.EndEdit();

                // ✅ SERIALIZA escrituras + reintenta locks
                DbWriteGate.RunWithRetry(() => _store.Save(_table));

                if (showFeedback)
                {
                    _table = _store.Load();
                    _bs.DataSource = _table;
                    MessageBox.Show("Ventas guardadas ✅", ModuleTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (showFeedback)
                    MessageBox.Show("Error guardando ventas ❌\n\n" + ex.Message, ModuleTitle,
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
