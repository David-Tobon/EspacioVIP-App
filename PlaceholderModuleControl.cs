using System.Drawing;
using System.Windows.Forms;

namespace Espacio_VIP_SL_App
{
    public class PlaceholderModuleControl : UserControl
    {
        public PlaceholderModuleControl(string titulo)
        {
            BackColor = Color.White;

            var label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 11f, FontStyle.Regular),
                Text = $"Aquí irá el módulo de: {titulo}"
            };

            Controls.Add(label);
        }
    }
}
