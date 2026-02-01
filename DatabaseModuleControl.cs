using System.Drawing;
using System.Windows.Forms;

namespace Espacio_VIP_SL_App
{
    public class DatabaseModuleControl : UserControl, IModuleSaveable
    {
        private TabControl _innerTabs;
        private CrudGridControl _clientesGrid;
        private CrudGridControl _proveedoresGrid;

        public string ModuleTitle => "Database";

        public bool HasUnsavedChanges =>
            (_clientesGrid?.HasUnsavedChanges ?? false) ||
            (_proveedoresGrid?.HasUnsavedChanges ?? false);

        public DatabaseModuleControl()
        {
            BackColor = Color.White;
            BuildUI();
        }

        private void BuildUI()
        {
            _innerTabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Padding = new Point(12, 6),
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(180, 40)
            };

            _innerTabs.Deselecting += (_, e) =>
            {
                // ✅ autosave al salir de sub-tab
                SaveSilent();
            };

            var tabClientes = new TabPage("Clientes") { BackColor = Color.White };
            var tabProveedores = new TabPage("Proveedores") { BackColor = Color.White };

            _clientesGrid = new CrudGridControl(
                titulo: "Clientes",
                columnas: new[] { "Id", "Nombre", "NIF/CIF", "Teléfono", "Email", "Dirección", "Notas" },
                store: new Data.Stores.ClientsStore()
            )
            { Dock = DockStyle.Fill };

            _proveedoresGrid = new CrudGridControl(
                titulo: "Proveedores",
                columnas: new[] { "Id", "Nombre", "NIF/CIF", "Teléfono", "Email", "Dirección", "Notas" },
                store: new Data.Stores.SuppliersStore()
            )
            { Dock = DockStyle.Fill };

            tabClientes.Controls.Add(_clientesGrid);
            tabProveedores.Controls.Add(_proveedoresGrid);

            _innerTabs.TabPages.Add(tabClientes);
            _innerTabs.TabPages.Add(tabProveedores);

            Controls.Add(_innerTabs);
        }

        public void SaveSilent()
        {
            _clientesGrid?.SaveSilent();
            _proveedoresGrid?.SaveSilent();
        }

        public bool SaveWithFeedback()
        {
            bool a = _clientesGrid?.SaveWithFeedback() ?? true;
            bool b = _proveedoresGrid?.SaveWithFeedback() ?? true;
            return a && b;
        }
    }
}
