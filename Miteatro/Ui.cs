using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Miteatro
{
    public class FormDialog : Window
    {
        readonly StackPanel fields = new StackPanel(); readonly Dictionary<string, Control> inputs = new Dictionary<string, Control>(); public Action Submit
        {
            get; set;
        }
        public FormDialog(Window owner, string title)
        {
            Owner = owner;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("es-MX");
            Title = title;
            Width = 520;
            Height = 650;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)new BrushConverter().ConvertFromString("#F9F7FA");
            FontFamily = new FontFamily("Segoe UI");
            var root = new DockPanel { Margin = new Thickness(24) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = Ui.Button("Guardar", () => { try { Submit?.Invoke(); DialogResult = true; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Mi Teatro", MessageBoxButton.OK, MessageBoxImage.Information); } });
            ok.IsDefault = true;
            actions.Children.Add(ok);
            var cancel = Ui.Button("Cancelar", () => DialogResult = false);
            cancel.IsCancel = true;
            actions.Children.Add(cancel);
            DockPanel.SetDock(actions, Dock.Bottom);
            root.Children.Add(actions);
            var heading = Ui.Text(title, 22);
            heading.Margin = new Thickness(0, 0, 0, 18);
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);
            root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = fields });
            Content = root;
        }
        void Add(string key, string label, Control control)
        {
            fields.Children.Add(Ui.Text(label));
            control.Margin = new Thickness(0, 5, 0, 14);
            control.Padding = new Thickness(8);
            fields.Children.Add(control);
            inputs.Add(key, control);
        }
        public void Text(string key, string label, string value = "")
        {
            Add(key, label, new TextBox { Text = value });
        }
        public void Secret(string key, string label)
        {
            Add(key, label, new PasswordBox());
        }
        public void Choice(string key, string label, IEnumerable<string> values, string selected = null)
        {
            var c = new ComboBox { ItemsSource = values.ToList() };
            c.SelectedItem = selected;
            if (c.SelectedIndex < 0)
                c.SelectedIndex = 0;
            Add(key, label, c);
        }
        public void Check(string key, string label, bool value)
        {
            Add(key, label, new CheckBox { IsChecked = value });
        }
        public string Get(string key)
        {
            var c = inputs[key];
            if (c is PasswordBox)
                return ((PasswordBox)c).Password;
            if (c is ComboBox)
                return (string)((ComboBox)c).SelectedItem ?? "";
            return ((TextBox)c).Text.Trim();
        }
        public bool Bool(string key) => ((CheckBox)inputs[key]).IsChecked == true;
        public decimal Number(string key)
        {
            decimal n;
            TheaterService.Require(decimal.TryParse(Get(key), out n), "Monto inválido: " + key);
            return n;
        }
        public int Integer(string key)
        {
            int n;
            TheaterService.Require(int.TryParse(Get(key), out n), "Cantidad entera inválida: " + key);
            return n;
        }
        public DateTime Date(string key)
        {
            DateTime n;
            TheaterService.Require(DateTime.TryParseExact(Get(key), "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out n), "Usa fecha y hora con formato dd/MM/aaaa HH:mm.");
            return n;
        }
    }
    public static class Ui
    {
        public static TextBlock Text(string text, double size = 14) => new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)new BrushConverter().ConvertFromString("#302637") };
        public static Button Button(string text, Action action)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 4, 8, 4), Background = (Brush)new BrushConverter().ConvertFromString("#6C3483"), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            b.Click += (s, e) => action();
            return b;
        }
        public static DataGrid Table()
        {
            var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E0D7E5"), RowHeight = 34, MinHeight = 120, MinColumnWidth = 85, MaxColumnWidth = 350 };
            var labels = new Dictionary<string, string> { { "Name", "Nombre" }, { "Starts", "Fecha y hora" }, { "Room", "Sala" }, { "Capacity", "Cupo" }, { "Adult", "Adulto" }, { "Child", "Niño" }, { "Senior", "Adulto mayor" }, { "Description", "Descripción" }, { "Restrictions", "Restricciones" }, { "Cancelled", "Cancelada" }, { "Price", "Precio" }, { "Stock", "Existencias" }, { "Minimum", "Mínimo" }, { "Enabled", "Activo" }, { "Code", "Código" }, { "Kind", "Tipo" }, { "Value", "Valor" }, { "From", "Inicio" }, { "Until", "Fin" }, { "Date", "Fecha" }, { "User", "Usuario" }, { "Action", "Acción" }, { "Detail", "Detalle" } };
            grid.AutoGeneratingColumn += (sender, e) => { if (e.PropertyName == "Id" || e.PropertyName.EndsWith("Id") || e.PropertyName == "PasswordHash" || e.PropertyName == "ImagePath") { e.Cancel = true; return; } if (labels.ContainsKey(e.PropertyName)) e.Column.Header = labels[e.PropertyName]; var text = e.Column as DataGridTextColumn; var type = Nullable.GetUnderlyingType(e.PropertyType) ?? e.PropertyType; if (text != null && type == typeof(DateTime)) ((System.Windows.Data.Binding)text.Binding).StringFormat = "dd/MM/yyyy HH:mm"; if (text != null && type == typeof(decimal)) ((System.Windows.Data.Binding)text.Binding).StringFormat = "N2"; };
            return grid;
        }
        public static StackPanel Row(params UIElement[] controls)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in controls)
                row.Children.Add(c);
            return row;
        }
    }
}



