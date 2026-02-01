using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Espacio_VIP_SL_App
{
    public partial class VIPForm : Form
    {
        private Panel _header;
        private Panel _separator;
        private PictureBox _logo;
        private Label _title;
        private TabControl _tabs;

        // Registro de módulos (cada tab = un UserControl)
        private Dictionary<string, Func<Control>> _moduleFactories;

        public VIPForm()
        {
            InitializeComponent();
            Data.DbBootstrap.Initialize(); // crea tablas Trabajos/Pagos
            InicializarUI();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // vacío por ahora
        }

        private void InicializarUI()
        {
            this.SuspendLayout();

            Text = "ESPACIO VIP - Sistema de Contabilidad";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 620);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;

            Controls.Clear();

            // ─────────────────────────────────────────────
            // HEADER (logo + título centrado)
            // ─────────────────────────────────────────────
            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.White,
                Padding = new Padding(14, 10, 14, 10)
            };

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.White
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _logo = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0),
                Image = CargarLogoSeguro("logo.png")
            };

            _title = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "ESPACIO VIP",
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(25, 25, 25)
            };

            var rightSpacer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

            headerLayout.Controls.Add(_logo, 0, 0);
            headerLayout.Controls.Add(_title, 1, 0);
            headerLayout.Controls.Add(rightSpacer, 2, 0);
            _header.Controls.Add(headerLayout);

            // Separador
            _separator = new Panel
            {
                Dock = DockStyle.Top,
                Height = 2,
                BackColor = Color.FromArgb(230, 230, 230)
            };

            // ─────────────────────────────────────────────
            // TAB CONTROL PRINCIPAL (carga módulos)
            // ─────────────────────────────────────────────
            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Padding = new Point(16, 8)

            };
            _tabs.Deselecting += (_, e) =>
            {
                AutoSaveInControl(e.TabPage);
            };
            _tabs.SizeMode = TabSizeMode.Fixed;
            _tabs.ItemSize = new Size(200, 48);
            _tabs.SelectedIndexChanged += (_, __) => EnsureSelectedTabLoaded();

            RegistrarModulos();
            ConstruirTabs();

            // Orden de agregado
            Controls.Add(_tabs);
            Controls.Add(_separator);
            Controls.Add(_header);

            EnsureSelectedTabLoaded(); // carga el primero
            this.ResumeLayout(true);

            this.FormClosing += VIPForm_FormClosing;

        }
        private void VIPForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            var saveables = FindSaveables(this).ToList();
            bool dirty = saveables.Any(m => m.HasUnsavedChanges);

            if (!dirty) return;

            var r = MessageBox.Show(
                "Hay cambios sin guardar.\n\n¿Quieres guardar antes de salir?",
                "ESPACIO VIP",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning
            );

            if (r == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (r == DialogResult.No)
            {
                // descartar
                return;
            }

            // Sí: guardar todo silencioso (sin spam de popups)
            foreach (var m in saveables)
            {
                if (!m.HasUnsavedChanges) continue;
                m.SaveSilent();
            }
        }

        private void AutoSaveInControl(Control? root)
        {
            if (root == null) return;

            foreach (var m in FindSaveables(root))
            {
                if (m.HasUnsavedChanges)
                    m.SaveSilent();
            }
        }

        private System.Collections.Generic.IEnumerable<IModuleSaveable> FindSaveables(Control root)
        {
            if (root is IModuleSaveable m)
                yield return m;

            foreach (Control c in root.Controls)
            {
                foreach (var child in FindSaveables(c))
                    yield return child;
            }
        }

        private void RegistrarModulos()
        {
            _moduleFactories = new Dictionary<string, Func<Control>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Facturas Venta"] = () => new PlaceholderModuleControl("Facturas Venta"),
                ["Registro Pagos"] = () => new PlaceholderModuleControl("Registro Pagos"),
                ["Compras"] = () => new PlaceholderModuleControl("Compras"),
                ["Registro Precios"] = () => new PlaceholderModuleControl("Registro Precios"),

                // ✅ NUEVO
                ["Database"] = () => new DatabaseModuleControl(),

                ["Facturas Venta"] = () => new VentasModuleControl(),
                ["Registro Pagos"] = () => new PagosModuleControl(),


            };
        }

        private void ConstruirTabs()
        {
            _tabs.TabPages.Clear();

            foreach (var kv in _moduleFactories)
            {
                var tab = new TabPage(kv.Key)
                {
                    BackColor = Color.White,
                    Tag = kv.Value // guardamos factory para lazy-load
                };
                _tabs.TabPages.Add(tab);
            }
        }

        private void EnsureSelectedTabLoaded()
        {
            if (_tabs.SelectedTab == null) return;

            var tab = _tabs.SelectedTab;

            // ya cargado
            if (tab.Controls.Count > 0) return;

            // si hay factory, crear control
            if (tab.Tag is Func<Control> factory)
            {
                var module = factory();
                module.Dock = DockStyle.Fill;

                tab.Controls.Add(module);

                // “consumimos” el factory para que no lo vuelva a crear
                tab.Tag = null;
            }
        }

        private Image CargarLogoSeguro(string fileName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(baseDir, fileName);

            try
            {
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    using (var ms = new MemoryStream(bytes))
                        return Image.FromStream(ms);
                }
            }
            catch { /* cae al placeholder */ }

            var bmp = new Bitmap(96, 64);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(245, 245, 245));
                using var pen = new Pen(Color.FromArgb(210, 210, 210));
                g.DrawRectangle(pen, 0, 0, bmp.Width - 1, bmp.Height - 1);

                using var f = new Font("Segoe UI", 18f, FontStyle.Bold);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var brush = new SolidBrush(Color.FromArgb(80, 80, 80));
                g.DrawString("EV", f, brush, new RectangleF(0, 0, bmp.Width, bmp.Height), sf);
            }
            return bmp;
        }
    }
}
