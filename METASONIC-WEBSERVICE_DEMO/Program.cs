using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace METASONIC_WEBSERVICE_DEMO
{
    internal static class Program
    {
        /// <summary>
        /// Der Haupteinstiegspunkt für die Anwendung.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0) // Falls ein Parameter übergeben wurde
            {
                string parameter = args[0]; // Erster Parameter
                Application.Run(new Form1(args[0].ToString()));
            }
            else
            {
                MessageBox.Show("Keine DOCID übergeben.");
                Application.Run(new Form1(""));
            }
          
        }
    }
}
