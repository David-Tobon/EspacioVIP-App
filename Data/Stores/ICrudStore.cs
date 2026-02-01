using System.Data;

namespace Espacio_VIP_SL_App.Data.Stores
{
    public interface ICrudStore
    {
        DataTable Load();
        void Save(DataTable table);
    }
}
