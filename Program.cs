using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DmsFormular1
{
    internal static class Program
    {
		/// <summary>
		/// Der Haupteinstiegspunkt für die Anwendung.
		/// </summary>
		/// 
		//static public string ProgramMode = "BRZ7live";
		static public string ProgramMode = "BRZ365live";
		//static public string ProgramMode = "BRZ365test";

		[STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
			try
			{
				if (args.Length > 0) // Falls ein Parameter übergeben wurde
                {
                    string parameter = args[0]; // Erster Parameter
                    Application.Run(new Form1(args[0].ToString().Replace("/","")));
                }
                else
                {
					switch (ProgramMode)
					{
						case "BRZ365test":
							Application.Run(new Form1("297")); // BRZ365 Testumgebung 10.0.0.5
							break;

						case "BRZ365live":
							Application.Run(new Form1("1527018")); // BRZ365
							break;

						case "BRZ7live":
							Application.Run(new Form1("101")); // BRZ7
							break;

						default:
							MessageBox.Show($"Ungültiger Modus: {ProgramMode}. Nutze BRZ365test.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
							Application.Run(new Form1("297")); // BRZ365 Testumgebung 10.0.0.5
							break;
					}


					// Todo Übergabe wieder aktivieren
					// Todo SecKey hinzufügen / Programmabbruch ohne 
					//MessageBox.Show("Keine DOCID übergeben.");
					//Application.Run(new Form1("297")); // BRZ365 Testumgebung 10.0.0.5
					//Application.Run(new Form1("1466254")); // BRZ365
					//Application.Run(new Form1("101")); //BRZ7

			    }

			}
			catch (Exception ex)
			{
				MessageBox.Show($"Fehler bei ApplicationRun Args0 = {args[0]} {ex.Message} {ex.StackTrace}");
			}

		}
    }
}
