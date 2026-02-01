using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Espacio_VIP_SL_App
{
    public class CrudGridControl : UserControl, IModuleSaveable
    {
        private readonly string _titulo;
        private readonly string[] _columnas;
        private readonly Data.Stores.ICrudStore? _store;

        private Panel _top;
        private Label _lblTitle;
        private TextBox _txtSearch;
        private Button _btnNewRow;
        private Button _btnDelete;
        private Button _btnSave;
        private Button _btnClear;

        private DataGridView _grid;
        private DataTable _table;
        private BindingSource _bs;

        public string ModuleTitle => _titulo;

        public bool HasUnsavedChanges
        {
            get
            {
                if (_store == null || _table == null) return false;
                return _table.GetChanges() != null;
            }
        }

        public CrudGridControl(string titulo, string[] columnas, Data.Stores.ICrudStore? store = null)
        {
            _titulo = titulo;
            _columnas = columnas;
            _store = store;

            BackColor = Color.White;
            BuildUI();
            BuildData();
        }

        private void BuildUI()
        {
            _top = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.White,
                Padding = new Padding(12, 10, 12, 10)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                BackColor = Color.White
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            _lblTitle = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = _titulo,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30)
            };

            _txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = "Buscar… (nombre, NIF, teléfono, email, etc.)",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular)
            };
            _txtSearch.TextChanged += (_, __) => ApplyFilter();

            _btnNewRow = new Button
            {
                Dock = DockStyle.Fill,
                Text = "Nuevo",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnNewRow.Click += (_, __) => AddRow();

            _btnDelete = new Button
            {
                Dock = DockStyle.Fill,
                Text = "Eliminar",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnDelete.Click += (_, __) => DeleteSelected();

            _btnSave = new Button
            {
                Dock = DockStyle.Fill,
                Text = "Guardar",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnSave.Click += (_, __) => SaveWithFeedback();

            _btnClear = new Button
            {
                Dock = DockStyle.Fill,
                Text = "Limpiar",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnClear.Click += (_, __) => ClearFilter();

            layout.Controls.Add(_lblTitle, 0, 0);
            layout.Controls.Add(_txtSearch, 1, 0);
            layout.Controls.Add(_btnNewRow, 2, 0);
            layout.Controls.Add(_btnDelete, 3, 0);
            layout.Controls.Add(_btnSave, 4, 0);
            layout.Controls.Add(_btnClear, 5, 0);

            _top.Controls.Add(layout);

            var separator = new Panel
            {
                Dock = DockStyle.Top,
                Height = 2,
                BackColor = Color.FromArgb(235, 235, 235)
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular)
            };

            Controls.Add(_grid);
            Controls.Add(separator);
            Controls.Add(_top);
        }

        private void BuildData()
        {
            _table = _store != null ? _store.Load() : CreateDemoTable();

            _bs = new BindingSource { DataSource = _table };
            _grid.DataSource = _bs;

            // Id bloqueado (si existe)
            if (_grid.Columns.Contains("Id"))
            {
                _grid.Columns["Id"].ReadOnly = true;
            }
        }

        private DataTable CreateDemoTable()
        {
            var dt = new DataTable(_titulo);
            foreach (var col in _columnas)
                dt.Columns.Add(col, typeof(string));

            dt.Rows.Add("1", "Ejemplo", "X1234567Z", "+34 600 000 000", "correo@ejemplo.com", "Barcelona", "Notas…");
            dt.AcceptChanges();
            return dt;
        }

        private void ApplyFilter()
        {
            if (_bs == null) return;

            var text = (_txtSearch.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                _bs.RemoveFilter();
                return;
            }

            var safe = text.Replace("'", "''");
            var parts = _columnas.Select(c => $"CONVERT([{c}], 'System.String') LIKE '%{safe}%'");
            _bs.Filter = string.Join(" OR ", parts);
        }

        private void ClearFilter()
        {
            _txtSearch.Text = "";
            _bs?.RemoveFilter();
        }

        private void AddRow()
        {
            var row = _table.NewRow();
            if (_table.Columns.Contains("Id"))
                row["Id"] = ""; // nuevo registro sin Id
            _table.Rows.Add(row);

            int idx = _grid.Rows.Count - 1;
            if (idx >= 0)
            {
                _grid.ClearSelection();
                _grid.Rows[idx].Selected = true;

                int colIndex = 0;
                if (_grid.Columns.Contains("Nombre"))
                    colIndex = _grid.Columns["Nombre"].Index;

                _grid.CurrentCell = _grid.Rows[idx].Cells[colIndex];
                _grid.BeginEdit(true);
            }
        }

        private void DeleteSelected()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Selecciona una o varias filas para eliminar.", _titulo,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var r = MessageBox.Show("¿Seguro que deseas eliminar las filas seleccionadas?",
                _titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (r != DialogResult.Yes) return;

            foreach (DataGridViewRow viewRow in _grid.SelectedRows)
            {
                if (viewRow.DataBoundItem is DataRowView drv)
                    drv.Row.Delete();
            }

            // ✅ NO AcceptChanges aquí: el store necesita ver RowState=Deleted
        }

        // ─────────────────────────────────────────────
        // Guardado: manual vs autosave
        // ─────────────────────────────────────────────
        public void SaveSilent()
        {
            SaveInternal(showFeedback: false);
        }

        public bool SaveWithFeedback()
        {
            return SaveInternal(showFeedback: true);
        }

        private bool SaveInternal(bool showFeedback)
        {
            if (_store == null)
            {
                if (showFeedback)
                {
                    MessageBox.Show("Este módulo no está conectado a SQLite (modo demo/memoria).", _titulo,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return false;
            }

            try
            {
                // Confirmar edición pendiente
                this.Validate();
                _grid.EndEdit();
                _bs.EndEdit();

                if (_grid.IsCurrentCellInEditMode)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    _grid.EndEdit(DataGridViewDataErrorContexts.Commit);
                }

                // Validación antes de persistir
                if (!ValidateBeforeSave(out var errorMsg, out int rowIndex, out string? colName))
                {
                    if (showFeedback)
                    {
                        MessageBox.Show(errorMsg, _titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        FocusCell(rowIndex, colName);
                    }
                    return false;
                }

                _store.Save(_table);

                // Recarga desde DB para reflejar la “verdad”
                _table = _store.Load();
                _bs.DataSource = _table;

                if (showFeedback)
                {
                    MessageBox.Show("Guardado en SQLite ✅\n\nDB:\n" + Data.AppDb.DbPath, _titulo,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (showFeedback)
                {
                    MessageBox.Show("Error guardando en SQLite ❌\n\n" + ex, _titulo,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }
        }

        private void FocusCell(int rowIndex, string? colName)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;

            int colIndex = 0;
            if (!string.IsNullOrWhiteSpace(colName) && _grid.Columns.Contains(colName))
                colIndex = _grid.Columns[colName].Index;

            _grid.ClearSelection();
            _grid.Rows[rowIndex].Selected = true;
            _grid.CurrentCell = _grid.Rows[rowIndex].Cells[colIndex];
            _grid.BeginEdit(true);
        }

        // ─────────────────────────────────────────────
        // Validación mínima (AAA-casera pero útil)
        // ─────────────────────────────────────────────
        private bool ValidateBeforeSave(out string message, out int rowIndex, out string? colName)
        {
            message = "";
            rowIndex = -1;
            colName = null;

            // Validamos filas nuevas/modificadas (y también las que están “medio llenas”)
            for (int i = 0; i < _table.Rows.Count; i++)
            {
                var row = _table.Rows[i];
                if (row.RowState == DataRowState.Deleted) continue;

                string nombre = Get(row, "Nombre");
                string email = Get(row, "Email");
                string telefono = Get(row, "Teléfono");

                // Detectar si fila está “vacía”
                bool anyData = _columnas.Any(c => !string.IsNullOrWhiteSpace(Get(row, c)));
                if (!anyData) continue;

                // Nombre obligatorio si hay algo en la fila
                if (string.IsNullOrWhiteSpace(nombre))
                {
                    message = "El campo 'Nombre' es obligatorio.";
                    rowIndex = i;
                    colName = "Nombre";
                    return false;
                }

                // Email válido (si existe)
                if (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email))
                {
                    message = "El 'Email' no tiene un formato válido.";
                    rowIndex = i;
                    colName = "Email";
                    return false;
                }

                // Teléfono: no letras, y mínimo algunos dígitos (si existe)
                if (!string.IsNullOrWhiteSpace(telefono))
                {
                    if (Regex.IsMatch(telefono, @"[A-Za-z]"))
                    {
                        message = "El 'Teléfono' no puede contener letras.";
                        rowIndex = i;
                        colName = "Teléfono";
                        return false;
                    }

                    int digits = telefono.Count(char.IsDigit);
                    if (digits > 0 && digits < 7)
                    {
                        message = "El 'Teléfono' parece demasiado corto (menos de 7 dígitos).";
                        rowIndex = i;
                        colName = "Teléfono";
                        return false;
                    }
                }
            }

            return true;
        }

        private static string Get(DataRow row, string col)
        {
            if (!row.Table.Columns.Contains(col)) return "";
            return (row[col]?.ToString() ?? "").Trim();
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var _ = new MailAddress(email);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
