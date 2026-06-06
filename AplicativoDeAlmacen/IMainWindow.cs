using System.Windows.Controls;

namespace AplicativoDeAlmacen
{
    public interface IMainWindow
    {
        void AbrirPestaña(string titulo, UserControl contenido);
    }
}