using DmsFormular1.ScanviewWebservice;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DmsFormular1
{
	public partial class Form1 : Form
    {
        private LoginData loginData;
		private ScanviewWebserviceClient scanview;

        String CurrentDocID;
		String CurrentProgramVersion = "1.0";
		/*String QualifierKreditorText = "89000207";
		String QualifierSachlich = "154";
		String QualifierBelegNr = "89000202";
		String QualifierPreislich = "89000604";
		String QualifierBetrag = "89000131"; // Netto
		String QualifierIsSplit = "234"??;*/


		// BRZ365 Testumgebung Metasonic 10.0.0.5
		String QualifierKreditorText = "89900207";
		String QualifierSachlich = "223";
		String QualifierBelegNr = "89900202";
		String QualifierPreislich = "222";
		String QualifierBetrag = "89900131";
		String QualifierIsSplit = "234";

		private const string StatusNOK = "falsch";
		private const string StatusOK = "richtig";
		private const string StatusUP = "ungeprüft";

		string sachlichStatusOnStart;
		string preislichStatusOnStart;

		String sachlichStatus = StatusUP; // Initialisiere auf 'UP'
		String preislichStatus = StatusUP; // Initialisiere auf 'UP'
		bool IsSplitBeleg = false;


		public Form1(string docid)
        {
            InitializeComponent();

			
			// Connect
			if (true)
			{
				switch (Program.ProgramMode)
				{
					case "BRZ365test":
						{
							var ep = Environment.GetEnvironmentVariable("METASONIC_ENDPOINT")
								?? "http://127.0.0.1:8089/ScanviewWebservice.svc";
							scanview = new ScanviewWebserviceClient("ScanviewWebserviceBasicHTTP", ep);
							var u = Environment.GetEnvironmentVariable("METASONIC_USER") ?? "";
							var p = Environment.GetEnvironmentVariable("METASONIC_PASSWORD") ?? "";
							var d = Environment.GetEnvironmentVariable("METASONIC_DB") ?? "";
							Connect(u, p, d);
						}

						break;

					case "BRZ365live":
						{
							var ep = Environment.GetEnvironmentVariable("METASONIC_ENDPOINT")
								?? "http://127.0.0.1:8089/ScanviewWebservice.svc";
							scanview = new ScanviewWebserviceClient("ScanviewWebserviceBasicHTTP", ep);
							var u = Environment.GetEnvironmentVariable("METASONIC_USER") ?? "";
							var p = Environment.GetEnvironmentVariable("METASONIC_PASSWORD") ?? "";
							var d = Environment.GetEnvironmentVariable("METASONIC_DB") ?? "";
							Connect(u, p, d);
						}

						break;

					case "BRZ7live":
						{
							var ep = Environment.GetEnvironmentVariable("METASONIC_ENDPOINT")
								?? "http://127.0.0.1:8089/ScanviewWebservice.svc";
							scanview = new ScanviewWebserviceClient("ScanviewWebserviceBasicHTTP", ep);
							var u = Environment.GetEnvironmentVariable("METASONIC_USER") ?? "";
							var p = Environment.GetEnvironmentVariable("METASONIC_PASSWORD") ?? "";
							var d = Environment.GetEnvironmentVariable("METASONIC_DB") ?? "";
							Connect(u, p, d);
						}

						break;

					default:
						{
							var ep = Environment.GetEnvironmentVariable("METASONIC_ENDPOINT")
								?? "http://127.0.0.1:8089/ScanviewWebservice.svc";
							scanview = new ScanviewWebserviceClient("ScanviewWebserviceBasicHTTP", ep);
							MessageBox.Show($"Ungültiger Modus: {Program.ProgramMode}. Nutze BRZ365test.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
							var u = Environment.GetEnvironmentVariable("METASONIC_USER") ?? "";
							var p = Environment.GetEnvironmentVariable("METASONIC_PASSWORD") ?? "";
							var d = Environment.GetEnvironmentVariable("METASONIC_DB") ?? "";
							Connect(u, p, d);
						}

						break;
				}


				
				 // test
			}

            CurrentDocID = docid;
			this.Text = $"Programmversion: {CurrentProgramVersion} | DocID: {CurrentDocID}";

			// Felder bespaßen
			// Todo: richtige Qualifizierer eintragen
			//textBoxGeschaeftspartner.Text = ReadQualifier(CurrentDocID, "89000207");
			//textBoxBelegnummer.Text = ReadQualifier(CurrentDocID, "89000202");

			this.FormBorderStyle = FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimumSize = this.Size;
			this.MaximumSize = this.Size;


			// Alle Qualifizierer zur DocID holen
			// Todo: Ist das schneller als 4-5x einzelabfrage?
			DataTable qualifiersTable = ReadAllQualifiers(CurrentDocID);
			Console.WriteLine($"{qualifiersTable.Rows.Count} Werte für DocID {CurrentDocID} gelesen.");

			// DEBUG alle Werte anzeigen
			ShowAllQualifiers(qualifiersTable);

			sachlichStatus = ExtractQualifierValue(qualifiersTable, QualifierSachlich);
			preislichStatus = ExtractQualifierValue(qualifiersTable, QualifierPreislich);
			IsSplitBeleg = string.Equals(ExtractQualifierValue(qualifiersTable, QualifierIsSplit), "JA", StringComparison.OrdinalIgnoreCase);

			textBoxGeschaeftspartner.Text = ExtractQualifierValue(qualifiersTable, QualifierKreditorText);
			textBoxBelegnummer.Text = ExtractQualifierValue(qualifiersTable, QualifierBelegNr);

			//string sachlichStatus = "UP";
			//string preislichStatus = "UP";
			//string sachlichStatus = ReadQualifier(CurrentDocID, QualifierSachlich);
			//string preislichStatus = ReadQualifier(CurrentDocID, QualifierPreislich);
			// In Mock Zeiten sicherstellen das UP drin steht wenn nix oder quatsch kommt
			// Überprüfe und setze den aktuellen Status
			sachlichStatus = (sachlichStatus == StatusOK || sachlichStatus == StatusNOK) ? sachlichStatus : StatusUP;
			preislichStatus = (preislichStatus == StatusOK || preislichStatus == StatusNOK) ? preislichStatus : StatusUP;

			// Status OnStart persistieren
			sachlichStatusOnStart = sachlichStatus;
			preislichStatusOnStart = preislichStatus;

			Console.WriteLine("Initialisiere Sachliche Prüfung auf Status: " + sachlichStatus);
			UpdateSachlichButtons(sachlichStatus);

			// Initialisiert die Preisliche Prüfung und gibt den Status in der Konsole aus
			Console.WriteLine("Initialisiere Preisliche Prüfung auf Status: " + preislichStatus);
			UpdatePreislichButtons(preislichStatus);

			// IsSplitContainer anzeigen oder ausblenden
			if (IsSplitBeleg)
			{
				panelSplit.Visible = true;
			}
			else
			{
				panelSplit.Visible = false;
			}

			//WriteQualifier("1470191", QualifierIsSplit, "JA");


		}


		// Qualifizierer DocClass Buchungen Sonstiges (BRZ7)
		// 89000001 = BetrNr
		// 89000207 = KredBezeichnung
		// 89000202 = BelegNr
		// 154 = Quickstart = in Mock Zeiten = sachliche Prüfung
		// 89000604 = Workflowstatus = in Mock Zeiten = preisliche Prüfung

		private void ShowAllQualifiers(DataTable qualifiersTable)
		{
			// Überprüfung, ob die DataTable nicht null und nicht leer ist
			if (qualifiersTable != null && qualifiersTable.Rows.Count > 0)
			{
				// Durch jede Zeile in der DataTable gehen
				foreach (DataRow row in qualifiersTable.Rows)
				{
					// Durch jede Spalte in der Zeile gehen
					foreach (DataColumn column in qualifiersTable.Columns)
					{
						// Zugriff auf den Wert in der aktuellen Zeile und Spalte
						object value = row[column];

						// Hier können Sie eine Operation mit dem Wert durchführen
						// Zum Beispiel: Ausgabe des Wertes
						Console.WriteLine($"Spalte: {column.ColumnName}, Wert: {value}");
					}
				}
			}
			else
			{
				Console.WriteLine("Die DataTable ist leer oder nicht initialisiert.");
			}
		}


		private void Connect(string user, string pass, string database)
		{
			// System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

			try
			{
				loginData = new LoginData
				{
					AutoLogin = true,
					NoSession = true,
					UseSSO = false,
					Username = user,
					Password = pass,
					LoginDatabaseName = database

				};
				scanview.Login(ref loginData);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Fehler bei Login @{database}. {ex.Message} {ex.StackTrace}");
			}
		}

		private ResponseDocument ReadDocument(string docid)
		{
			return scanview.GetDocument(ref loginData, docid, false, false, true, false);
		}


		private DataTable ReadAllQualifiers(string docid)
		{
			var document = scanview.GetDocument(ref loginData, docid, false, false, true, false);
			DataTable qualifiersTable = new DataTable();
			qualifiersTable.Columns.Add("ID", typeof(string));
			qualifiersTable.Columns.Add("Value", typeof(string));

			foreach (var field in document.FieldList)
			{
				qualifiersTable.Rows.Add(field.ID, field.Value);
			}

			return qualifiersTable;
		}

		private string ExtractQualifierValue(DataTable qualifiersTable, string qualifierId)
		{
			DataRow[] foundRows = qualifiersTable.Select($"ID = '{qualifierId}'");
			if (foundRows.Length > 0)
			{
				return foundRows[0]["Value"].ToString();
			}
			else
			{
				return string.Empty;
			}
		}


		private string ReadQualifier(string docid, string qualifierId)
		{
			var document = scanview.GetDocument(ref loginData, docid, false, false, true, false);
			var qualifier = document.FieldList.FirstOrDefault(field => field.ID == qualifierId);
			return qualifier?.Value ?? string.Empty; // Gibt den Wert zurück oder einen leeren String, falls nicht gefunden
		}

		private bool WriteQualifier(string docid, string qualifierId, string value)
		{
			try
			{
				var fields = new List<RequestField> { new RequestField { ID = qualifierId, Value = value } };
				scanview.UpdateDocumentFields(ref loginData, docid, fields.ToArray());
				return true;
			}
			catch (FaultException e) // Fängt spezifische SOAP-Exceptions
			{
				MessageBox.Show($"Fehler beim Schreiben des Dokuments: {e.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
			catch (Exception e) // Fängt allgemeine Exceptions
			{
				MessageBox.Show($"Allgemeiner Fehler: {e.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}
		
		private async Task<bool> WriteQualifierAsync(string docid, string qualifierId, string value)
		{
			try
			{
				var fields = new List<RequestField> { new RequestField { ID = qualifierId, Value = value } };
				await Task.Run(() => scanview.UpdateDocumentFields(ref loginData, docid, fields.ToArray()));
				return true;
			}
			catch (FaultException e) // Fängt spezifische SOAP-Exceptions
			{
				MessageBox.Show($"Fehler beim Schreiben des Dokuments: {e.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
			catch (Exception e) // Fängt allgemeine Exceptions
			{
				MessageBox.Show($"Allgemeiner Fehler: {e.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}

		private void UpdateSachlichButtons(string status)
		{
			// Setze alle Buttons in der Gruppe auf die Standardfarbe
			buttonSachlichJA.BackColor = SystemColors.Control;
			buttonSachlichNEIN.BackColor = SystemColors.Control;
			buttonSachlichOffen.BackColor = SystemColors.Control;

			// Setze die Farbe des ausgewählten Buttons basierend auf dem Status
			switch (status)
			{
				case StatusOK:
					buttonSachlichJA.BackColor = Color.Green;
					Console.WriteLine("buttonSachlichJA wurde auf Status OK gesetzt.");
					break;
				case StatusNOK:
					buttonSachlichNEIN.BackColor = Color.Red;
					Console.WriteLine("buttonSachlichNEIN wurde auf Status NOK gesetzt.");
					break;
				case StatusUP:
					buttonSachlichOffen.BackColor = Color.DarkOrange;
					Console.WriteLine("buttonSachlichOffen wurde auf Status UP gesetzt.");
					break;
			}
			textBoxNaechsteStation.Text = GetNextStation();
		}

		private void UpdatePreislichButtons(string status)
		{
			// Setze alle Buttons in der Gruppe auf die Standardfarbe
			buttonPreislichJA.BackColor = SystemColors.Control;
			buttonPreislichNEIN.BackColor = SystemColors.Control;
			buttonPreislichOffen.BackColor = SystemColors.Control;

			// Setze die Farbe des ausgewählten Buttons basierend auf dem Status
			switch (status)
			{
				case StatusOK:
					buttonPreislichJA.BackColor = Color.Green;
					Console.WriteLine("buttonPreislichJA wurde auf Status OK gesetzt.");
					break;
				case StatusNOK:
					buttonPreislichNEIN.BackColor = Color.Red;
					Console.WriteLine("buttonPreislichNEIN wurde auf Status NOK gesetzt.");
					break;
				case StatusUP:
					buttonPreislichOffen.BackColor = Color.DarkOrange;
					Console.WriteLine("buttonPreislichOffen wurde auf Status UP gesetzt.");
					break;
			}
			textBoxNaechsteStation.Text = GetNextStation();
		}


		private ResponseDocument GetDocument(string docid)
        {
            ResponseDocument doc = scanview.GetDocument(ref loginData, docid, false, false, true, false);
            return doc;
        }


		private void buttonSachlichJA_Click(object sender, EventArgs e)
		{
			// Die eigentlich richtigere Variante aber wirkt laggy
			/*buttonSachlichJA.Enabled = false;
			if (await WriteQualifierAsync(CurrentDocID, QualifierSachlich, "OK"))
			{
				sachlichStatus = "OK"; // Aktualisiere den globalen Status
				UpdateSachlichButtons("OK");
			}
			buttonSachlichJA.Enabled = true;*/

			// Wenn SplitBeleg und sachlich bereits falsch ist, darf nicht ja und nicht ungeprüft gesetzt werden
			// Aber nur wenn es beim Start schon so war
			if (sachlichStatusOnStart == StatusNOK && IsSplitBeleg)
			{
				// Sorry, status war bei start schon falsch und kann hier nicht auf ok geändert werden.
				MessageBox.Show("Split Beleg war bei Start schon auf sachlich falsch - er kann daher nicht mehr auf richtig geändert werden.");
			}
			else
			{
				// wenn start status nicht NOK war egal was ausgewählt wird
				sachlichStatus = StatusOK;
				UpdateSachlichButtons(sachlichStatus);
				WriteQualifier(CurrentDocID, QualifierSachlich, sachlichStatus);
			}


		}

		private void buttonSachlichNEIN_Click(object sender, EventArgs e)
		{
			sachlichStatus = StatusNOK;
			UpdateSachlichButtons(sachlichStatus);
			WriteQualifier(CurrentDocID, QualifierSachlich, sachlichStatus);
		}

		private void buttonSachlichOffen_Click(object sender, EventArgs e)
		{
			// Wenn SplitBeleg und sachlich bereits falsch ist, darf nicht ja und nicht ungeprüft gesetzt werden
			// Aber nur wenn es beim Start schon so war
			if (sachlichStatusOnStart == StatusNOK && IsSplitBeleg)
			{
				// Sorry, status war bei start schon falsch und kann hier nicht auf ok geändert werden.
				MessageBox.Show("Split Beleg war bei Start schon auf sachlich falsch - er kann daher nicht mehr auf ungeprüft geändert werden.");
			}
			else
			{
				// wenn start status nicht NOK war egal was ausgewählt wird
				sachlichStatus = StatusUP;
				UpdateSachlichButtons(sachlichStatus);
				WriteQualifier(CurrentDocID, QualifierSachlich, sachlichStatus);
				//await WriteQualifierAsync(CurrentDocID, QualifierSachlich, sachlichStatus);
			}

		}

		//private async void buttonPreislichJA_Click(object sender, EventArgs e)
		private void buttonPreislichJA_Click(object sender, EventArgs e)
		{
			// Wenn SplitBeleg und sachlich bereits falsch ist, darf nicht ja und nicht ungeprüft gesetzt werden
			// Aber nur wenn es beim Start schon so war
			if (preislichStatusOnStart == StatusNOK && IsSplitBeleg)
			{
				// Sorry, status war bei start schon falsch und kann hier nicht auf ok geändert werden.
				MessageBox.Show("Split Beleg war bei Start schon auf preislich falsch - er kann daher nicht mehr auf richtig geändert werden.");
			}
			else
			{
				// wenn start status nicht NOK war egal was ausgewählt wird
				preislichStatus = StatusOK;
				UpdatePreislichButtons(preislichStatus);
				WriteQualifier(CurrentDocID, QualifierPreislich, preislichStatus);
			}
		}

		private void buttonPreislichNEIN_Click(object sender, EventArgs e)
		{
			preislichStatus = StatusNOK;
			UpdatePreislichButtons(preislichStatus);
			WriteQualifier(CurrentDocID, QualifierPreislich, preislichStatus);
		}

		private void buttonPreislichOffen_Click(object sender, EventArgs e)
		{
			// Wenn SplitBeleg und sachlich bereits falsch ist, darf nicht ja und nicht ungeprüft gesetzt werden
			// Aber nur wenn es beim Start schon so war
			if (preislichStatusOnStart == StatusNOK && IsSplitBeleg)
			{
				// Sorry, status war bei start schon falsch und kann hier nicht auf ok geändert werden.
				MessageBox.Show("Split Beleg war bei Start schon auf preislich falsch - er kann daher nicht mehr auf ungeprüft geändert werden.");
			}
			else
			{
				// wenn start status nicht NOK war egal was ausgewählt wird
				preislichStatus = StatusUP;
				UpdatePreislichButtons(preislichStatus);
				WriteQualifier(CurrentDocID, QualifierPreislich, preislichStatus);
			}
		}

		private void OpenUrlInDefaultBrowser(string url)
		{
			try
			{
				// Startet den Standardbrowser mit der angegebenen URL
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
				{
					FileName = url,
					UseShellExecute = true // Wichtig, um den Standardbrowser zu verwenden
				});
			}
			catch (Exception ex)
			{
				// Fehlerbehandlung, falls das Öffnen des Browsers fehlschlägt
				MessageBox.Show($"Konnte den Browser nicht öffnen: {ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private string GetNextStation()
		{
			// Todo: schauen an welcher station ein beleg sich gerade befindet, je nach dem ist die nächste station eine andere
			if (sachlichStatus == StatusUP && preislichStatus == StatusUP)
				return "Lieferscheinkontrolle";
			if (sachlichStatus == StatusOK && preislichStatus == StatusUP)
				return "Einkauf";
			if (sachlichStatus == StatusNOK && preislichStatus == StatusUP)
				return "Einkauf, danach Rechnungsänderung";
			if (sachlichStatus == StatusUP && preislichStatus == StatusOK)
				return "Lieferscheinkontrolle";
			if (sachlichStatus == StatusUP && preislichStatus == StatusNOK)
				return "Lieferscheinkontrolle, danach Rechnungsänderung";
			if (sachlichStatus == StatusOK && preislichStatus == StatusOK)
				return "Zahlung";
			if (sachlichStatus == StatusNOK && preislichStatus == StatusNOK)
				return "Rechnungsänderung";
			if (sachlichStatus == StatusOK && preislichStatus == StatusNOK)
				return "Rechnungsänderung";
			if (sachlichStatus == StatusNOK && preislichStatus == StatusOK)
				return "Rechnungsänderung";

			return "Status unbekannt";
		}


		private void buttonBelegFinance_Click(object sender, EventArgs e)
		{
			// Default Browser öffnen und Finance URL aufrufen
			//OpenUrlInDefaultBrowser("https://google.de");
			
			//string Belegnummer = document.FieldList["89900202"].Value.ToString();
			string Belegnummer = textBoxBelegnummer.Text;
			OpenUrlInDefaultBrowser($"https://finance-s002.brz365.eu/syracuse-main/html/main.html?url=/trans/x3/erp/L240604/$sessions?f=GESBIS/2//M/ + {Belegnummer}");

		}

		private void labelSplit_Click(object sender, EventArgs e)
		{
			//OpenUrlInDefaultBrowser("https://google.de");
			OpenUrlInDefaultBrowser("https://qwiki.reif-bau.de/content/56262aa3-22a2-4ac3-8406-cb97911f74a2?query=brz365&title=sammelseite-fr-brz365-prozesse");
		}
	}
    
}
