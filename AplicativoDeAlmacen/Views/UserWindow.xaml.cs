using System;
using System.Data.SqlClient;
using System.Windows;

namespace AplicativoDeAlmacen.Views
{
    public partial class UserWindow : Window
    {
        // Definir la cadena de conexión aquí (reemplaza con tu propia cadena de conexión)
        
        private string connectionString = @"Data Source=DESKTOP-AI2LEQI;Initial Catalog=EdicionesPizaControl;Integrated Security=True;";
        public UserWindow()
        {
            InitializeComponent();
        }

        private void BtnRegistrarUsuario_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text;
            string nombres = TxtNombres.Text;
            string password = TxtPassword.Password;
            int rolUsuarioId = CmbTipoUsuario.SelectedIndex + 1; // Asumiendo que los IDs son 1 para Admin, 2 para Asistente

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(nombres) || string.IsNullOrWhiteSpace(password) || CmbTipoUsuario.SelectedIndex == -1)
            {
                MessageBox.Show("Por favor, complete todos los campos.");
                return;
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string query = "INSERT INTO usuarios (username, nombres, password, created_at, updated_at, rol_usuario_id) " +
                                   "VALUES (@Username, @Nombres, @Password, GETDATE(), GETDATE(), @RolUsuarioId)";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Nombres", nombres);
                        command.Parameters.AddWithValue("@Password", password); // Considera encriptar la contraseña
                        command.Parameters.AddWithValue("@RolUsuarioId", rolUsuarioId);

                        int result = command.ExecuteNonQuery();
                        if (result > 0)
                        {
                            MessageBox.Show("Usuario registrado exitosamente.");
                            this.Close(); // Cierra la ventana después de registrar
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al registrar usuario: {ex.Message}");
            }
        }

        private void BtnCancelarRegistro_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Simplemente cierra la ventana
        }

        private void LimpiarCampos()
        {
            TxtUsername.Text = string.Empty;
            TxtNombres.Text = string.Empty;
            TxtPassword.Password = string.Empty;
            CmbTipoUsuario.SelectedIndex = -1;
        }
    }
}
