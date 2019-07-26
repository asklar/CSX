using Microsoft.Toolkit.Forms.UI.XamlHost;
using System.Windows.Forms;

namespace Test
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            Windows.UI.Xaml.Hosting.WindowsXamlManager.InitializeForCurrentThread();
            var host = new WindowsXamlHost();
            host.Child = (new Demo()).Do();
            host.Width = this.Width - this.Margin.Horizontal * 2;
            host.Height = this.Height;
            this.Controls.Add(host);
        }

    }
}
