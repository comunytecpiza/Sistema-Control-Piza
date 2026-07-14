using AplicativoDeAlmacen.Models;
using AplicativoDeAlmacen.Models.Facturación; // O donde esté tu ItemGridDTO
using AplicativoDeAlmacen.Services.Documentos;
using AplicativoDeAlmacen.Services.facturaciòn;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace AplicativoDeAlmacen.Views.Movimientos.RegistroComprobante
{
    public partial class LectorWindow : Window
    {
        private readonly FacturacionService _facturacionService;
        private readonly ObservableCollection<ItemGridDTO> _gridOriginal; // Referencia a la grilla principal
        private bool _isProcessing = false;

        public LectorWindow(ObservableCollection<ItemGridDTO> gridOriginal)
        {
            InitializeComponent();
            _facturacionService = new FacturacionService();
            _gridOriginal = gridOriginal;

            // Forzar el foco al TextBox para que la lectora escriba ahí directamente
            Loaded += (s, e) => TxtLector.Focus();
        }

        private async void TxtLector_KeyDown(object sender, KeyEventArgs e)
        {
            // La lectora de barras manda un ENTER automático al final
            if (e.Key == Key.Enter && !_isProcessing)
            {
                string codigo = TxtLector.Text.Trim();
                if (string.IsNullOrEmpty(codigo)) return;

                _isProcessing = true;
                TxtLector.IsEnabled = false; // Bloqueamos milisegundos para evitar dobles lecturas

                try
                {
                    // 1. Buscar en BD
                    var resultado = await _facturacionService.ProcesarCodigoPorLectoraAsync(codigo);

                    // 2. Revisar si EL CÓDIGO ya está en toda la grilla principal
                    bool codigoYaExiste = _gridOriginal.Any(item => item.Codigos.Any(c => c.CodigoCreadoId == resultado.CodigoCreadoId));
                    if (codigoYaExiste)
                    {
                        AgregarLog($"⚠️ ERROR: El código {resultado.CodigoCompleto} ya fue escaneado en esta sesión.", true);
                    }
                    else
                    {
                        // 3. Agrupar en la grilla
                        ProcesarEnGrilla(resultado);
                        AgregarLog($"✅ OK: {resultado.DescripcionProducto} ({resultado.CodigoCompleto})");
                    }
                }
                catch (Exception ex)
                {
                    AgregarLog($"❌ RECHAZADO: {ex.Message}", true);
                    System.Media.SystemSounds.Beep.Play(); // Sonido de error
                }
                finally
                {
                    TxtLector.Text = "";
                    TxtLector.IsEnabled = true;
                    TxtLector.Focus();
                    _isProcessing = false;
                }
            }
        }

        private void ProcesarEnGrilla(LectoraResultDTO resultado)
        {
            // ¿El producto ya está en la grilla?
            var itemExistente = _gridOriginal.FirstOrDefault(i => i.ProductoId == resultado.ProductoId);

            if (itemExistente != null)
            {
                // Solo le agregamos el código a la lista interna
                itemExistente.Codigos.Add(new CodigoLeidoDTO
                {
                    CodigoCreadoId = resultado.CodigoCreadoId,
                    CodigoString = resultado.CodigoCompleto,
                    Cantidad = 1,
                    Coleccion = "Lectora Automática"
                });

                // Actualizamos las cantidades
                itemExistente.CanProd = itemExistente.Codigos.Count;
                itemExistente.ImpTota = itemExistente.CanProd * itemExistente.PreUnit;

                // TRUCO WPF: Remover y volver a agregar para que la grilla visual se actualice
                int index = _gridOriginal.IndexOf(itemExistente);
                _gridOriginal.RemoveAt(index);
                _gridOriginal.Insert(index, itemExistente);
            }
            else
            {
                // Es un producto nuevo, creamos el Item
                var nuevoItem = new ItemGridDTO
                {
                    ProductoId = resultado.ProductoId,
                    MovimientoId = resultado.MovimientoId,
                    DescripcionProducto = resultado.DescripcionProducto,
                    UnidadMedida = resultado.UnidadMedida,
                    CanProd = 1,
                    PreUnit = resultado.PrecioUnitario,
                    ImpTota = resultado.PrecioUnitario * 1
                };

                nuevoItem.Codigos.Add(new CodigoLeidoDTO
                {
                    CodigoCreadoId = resultado.CodigoCreadoId,
                    CodigoString = resultado.CodigoCompleto,
                    Cantidad = 1,
                    Coleccion = "Lectora Automática"
                });

                _gridOriginal.Add(nuevoItem);
            }
        }

        private void AgregarLog(string mensaje, bool esError = false)
        {
            // Insertar arriba para ver lo último siempre
            LstLog.Items.Insert(0, new { Texto = mensaje, Hora = DateTime.Now.ToString("HH:mm:ss") });
            // Esto es asumiendo un ListBox simple. Puedes mejorarlo con DataTemplates.
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}