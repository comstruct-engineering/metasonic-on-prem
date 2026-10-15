using METASONIC_WEBSERVICE_DEMO.ScanviewWebservice;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace METASONIC_WEBSERVICE_DEMO
{
    public partial class Form1 : Form
    {
        private LoginData loginData;
        private BasicHttpBinding binding = new BasicHttpBinding();
        private EndpointAddress endpoint;
        private ScanviewWebserviceClient scanview;

        public Form1(string docid)
        {
            InitializeComponent();
            var ep =
                Environment.GetEnvironmentVariable("METASONIC_ENDPOINT")
                ?? "http://127.0.0.1:8089/ScanviewWebservice.svc";
            endpoint = new EndpointAddress(ep);
            scanview = new ScanviewWebserviceClient("ScanviewWebserviceBasicHTTP", ep);
            var u = Environment.GetEnvironmentVariable("METASONIC_USER") ?? "";
            var p = Environment.GetEnvironmentVariable("METASONIC_PASSWORD") ?? "";
            var d = Environment.GetEnvironmentVariable("METASONIC_DB") ?? "";
            Connect(u, p, d);
            txt_LoadedDocID.Text = docid;
            if (docid != "")
            {
                FillGridView(docid);
            }
        }


        private void FillGridView(string docid)
        {
            dataGridView1.Rows.Clear();
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            if (dataGridView1.Columns.Count == 0)
            {
                dataGridView1.Columns.Add("ID", "ID");
                  dataGridView1.Columns.Add("Wert", "WERT");
            }
            var response = scanview.GetDocument(ref loginData, docid, false, false, true, false).FieldList;
            foreach (var item in response)
            {
                dataGridView1.Rows.Add(item.ID, item.Value);
            }
        }
        private void Connect(string user, string pass, string database)
        {
           // System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

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
            //scanview.Login(ref loginData);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            dataGridView1.Rows.Clear();
           ResponseDocument doc=  GetDocument(txt_LoadedDocID.Text);
            
            FillGridView(doc.ID);

        }
        private ResponseDocument GetDocument(string docid)
        {
            ResponseDocument doc = scanview.GetDocument(ref loginData, docid, false, false, true, false);
            return doc;
        }
        private void dataGridView1_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
             
                txt_selectedQid.Text = dataGridView1.Rows[e.RowIndex].Cells[0].Value.ToString();
                txt_OldQidValue.Text = dataGridView1.Rows[e.RowIndex].Cells[1].Value.ToString();
                txt_newQidValue.Text = dataGridView1.Rows[e.RowIndex].Cells[1].Value.ToString();
            }
        }

        private void btn_SaveDoc_Click(object sender, EventArgs e)
        {
            string Qid = txt_selectedQid.Text;
            string newQidValue = txt_newQidValue.Text;
            ResponseDocument doc = scanview.GetDocument(ref loginData,txt_LoadedDocID.Text, false,false,true,false);
            List<RequestField>  UpdatedFields= new List<RequestField>();
            UpdatedFields.Add(new RequestField{ ID = Qid, Value = newQidValue });
            scanview.UpdateDocumentFields(ref loginData,doc.ID,UpdatedFields.ToArray());
            FillGridView(doc.ID);

        }

        private void btn_LoadQid_Click(object sender, EventArgs e)
        {
            ResponseDocument doc = GetDocument(txt_LoadedDocID.Text);
            ResponseDocumentField qid= doc.FieldList.FirstOrDefault(x => x.ID == txt_selectedQid.Text);
            txt_OldQidValue.Text = qid.Value.ToString();
            txt_newQidValue.Text=qid.Value.ToString();

        }
    }
    
}
