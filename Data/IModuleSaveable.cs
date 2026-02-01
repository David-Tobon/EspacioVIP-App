using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Espacio_VIP_SL_App
{
    public interface IModuleSaveable
    {
        string ModuleTitle { get; }
        bool HasUnsavedChanges { get; }
        void SaveSilent(); // guardar sin popups (autosave)
        bool SaveWithFeedback(); // guardar con feedback (botón Guardar)
    }
}
