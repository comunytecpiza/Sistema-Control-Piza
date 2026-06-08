using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ClosedXML.Excel;
using Microsoft.Win32; // <--- Esta es la librería correcta para WPF

namespace AplicativoDeAlmacen.Views
{
    /// <summary>
    /// Lógica de interacción para ImportarCodigos.xaml
    /// </summary>
    public partial class ImportarCodigos : Window
    {
        // Propiedad pública para que la ventana principal pueda leer los datos
        public List<string> CodigosImportados { get; set; } = new List<string>();
        public ImportarCodigos()
        {
            InitializeComponent();
        }

        private DataTable LeerExcel(string ruta)
        {
            DataTable dt = new DataTable();
            using (XLWorkbook workbook = new XLWorkbook(ruta))
            {
                var worksheet = workbook.Worksheet(1);
                var primeraFila = worksheet.FirstRowUsed();

                // Crear Columnas
                foreach (var celda in primeraFila.Cells())
                {
                    dt.Columns.Add(celda.Value.ToString());
                }

                // Crear Filas (saltando la primera que es el encabezado)
                foreach (var fila in worksheet.RowsUsed().Skip(1))
                {
                    DataRow dr = dt.NewRow();
                    int i = 0;
                    foreach (var celda in fila.Cells(1, dt.Columns.Count))
                    {
                        dr[i] = celda.Value.ToString();
                        i++;
                    }
                    dt.Rows.Add(dr);
                }
            }
            return dt;
        }

        private DataTable LeerTXT(string ruta)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("Codigo"); // Nombre de la columna que verás en el DataGrid

            string[] lineas = System.IO.File.ReadAllLines(ruta);
            foreach (string linea in lineas)
            {
                if (!string.IsNullOrWhiteSpace(linea))
                    dt.Rows.Add(linea.Trim());
            }
            return dt;
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            // Filtro para mostrar ambos tipos de archivos
            openFileDialog.Filter = "Archivos Permitidos (*.xlsx; *.txt)|*.xlsx;*.txt";

            if (openFileDialog.ShowDialog() == true)
            {
                // 1. Ponemos la ruta en el TextBox (el que dice 373 en tu imagen)
                txtRutaArchivo.Text = openFileDialog.FileName;

                // 2. Detectamos la extensión
                string extension = System.IO.Path.GetExtension(openFileDialog.FileName).ToLower();
                DataTable dt = new DataTable();

                try
                {
                    if (extension == ".xlsx")
                    {
                        dt = LeerExcel(openFileDialog.FileName);
                    }
                    else if (extension == ".txt")
                    {
                        dt = LeerTXT(openFileDialog.FileName);
                    }

                    // 3. Cargamos el DataGrid y el contador
                    dgDatos.ItemsSource = dt.DefaultView;
                    txtTotalCodigos.Text = dt.Rows.Count.ToString();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al leer el archivo: " + ex.Message);
                }
            }
        }


        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            // Llenamos nuestra lista pública con lo que hay en el DataGrid
            foreach (DataRowView fila in dgDatos.ItemsSource)
            {
                CodigosImportados.Add(fila[0].ToString()); // Columna 0 es el código
            }

            // Indicamos que la operación fue exitosa y cerramos
            this.DialogResult = true;
            this.Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

        }

       
    }
}
