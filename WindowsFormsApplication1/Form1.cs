using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WindowsFormsApplication1
{
    public partial class Form1 : Form
    {
        //int maxCommandLength = 0;
        DataTable CommandDatabase = new DataTable();
        DataTable ErrorsDatabase = new DataTable();
        DataTable ResultDatabase = new DataTable();

        string SourceFile = "default.txt";
        List<byte> sourceData = new List<byte>();

        public class ResultColumns
        {
            public static int Description { get; set; } = 0;
            public static int Value { get; set; } = 1;
            public static int Type { get; set; } = 2;
            public static int Raw { get; set; } = 3;
        }

        public Form1()
        {
            InitializeComponent();
            textBox_code.Select(0, 0);
            defaultCSVToolStripTextBox.Text = PayOnlineFiscalParser.Properties.Settings.Default.CommandsDatabaseFile;
            errorsCSV_toolStripTextBox.Text = PayOnlineFiscalParser.Properties.Settings.Default.ErrorsDatabaseFile;
            ReadCsv(defaultCSVToolStripTextBox.Text, CommandDatabase);
            for (int i = 0; i < CommandDatabase.Rows.Count; i++) CommandDatabase.Rows[i][0] = Accessory.CheckHexString(CommandDatabase.Rows[i][0].ToString());
            dataGridView_commands.DataSource = CommandDatabase;

            dataGridView_result.DataSource = ResultDatabase;
            dataGridView_commands.ReadOnly = true;
            ResultDatabase.Columns.Add("Desc");
            ResultDatabase.Columns.Add("Value");
            ResultDatabase.Columns.Add("Type");
            ResultDatabase.Columns.Add("Raw");
            ParseEscPos.commandDataBase = CommandDatabase;
            ParseEscPos.sourceData.AddRange(Accessory.ConvertHexToByteArray(textBox_code.Text));
            for (int i = 0; i < dataGridView_commands.Columns.Count; i++) dataGridView_commands.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
            for (int i = 0; i < dataGridView_result.Columns.Count; i++) dataGridView_result.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;

            ReadCsv(PayOnlineFiscalParser.Properties.Settings.Default.ErrorsDatabaseFile, ErrorsDatabase);
        }

        public void ReadCsv(string fileName, DataTable table)
        {
            table.Clear();
            table.Columns.Clear();
            FileStream inputFile;
            try
            {
                inputFile = File.OpenRead(fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening file:" + fileName + " : " + ex.Message);
                return;
            }

            //read headers
            StringBuilder inputStr = new StringBuilder();
            int c = inputFile.ReadByte();
            while (c != '\r' && c != '\n' && c != -1)
            {
                byte[] b = new byte[1];
                b[0] = (byte)c;
                inputStr.Append(Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage).GetString(b));
                c = inputFile.ReadByte();
            }

            //create and count columns and read headers
            int colNum = 0;
            if (inputStr.Length != 0)
            {
                string[] cells = inputStr.ToString().Split(PayOnlineFiscalParser.Properties.Settings.Default.CSVdelimiter);
                colNum = cells.Length - 1;
                for (int i = 0; i < colNum; i++)
                {
                    table.Columns.Add(cells[i]);
                }
            }

            //read CSV content string by string
            while (c != -1)
            {
                int i = 0;
                c = 0;
                inputStr.Length = 0;
                while (i < colNum && c != -1 /*&& c != '\r' && c != '\n'*/)
                {
                    c = inputFile.ReadByte();
                    byte[] b = new byte[1];
                    b[0] = (byte)c;
                    if (c == PayOnlineFiscalParser.Properties.Settings.Default.CSVdelimiter) i++;
                    if (c != -1) inputStr.Append(Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage).GetString(b));
                }
                while (c != '\r' && c != '\n' && c != -1) c = inputFile.ReadByte();
                if (inputStr.ToString().Replace(PayOnlineFiscalParser.Properties.Settings.Default.CSVdelimiter, ' ').Trim().TrimStart('\r').TrimStart('\n').TrimEnd('\n').TrimEnd('\r') != "")
                {
                    string[] cells = inputStr.ToString().Split(PayOnlineFiscalParser.Properties.Settings.Default.CSVdelimiter);

                    DataRow row = table.NewRow();
                    for (i = 0; i < cells.Length - 1; i++)
                    {
                        row[i] = cells[i].TrimStart('\r').TrimStart('\n').TrimEnd('\n').TrimEnd('\r');
                    }
                    table.Rows.Add(row);
                }
            }
            inputFile.Close();
        }

        private void Button_find_Click(object sender, EventArgs e)
        {
            textBox_command.Text = "";
            textBox_commandDesc.Clear();
            ResultDatabase.Clear();
            if (textBox_code.SelectionStart != textBox_code.Text.Length) //check if cursor position in not last
            {
                if (textBox_code.Text.Substring(textBox_code.SelectionStart, 1) == " ")
                {
                    textBox_code.SelectionStart++;
                }
            }
            if (textBox_code.SelectionStart != 0) //check if cursor position in not first
            {
                if (textBox_code.Text.Substring(textBox_code.SelectionStart - 1, 1) != " " && textBox_code.Text.Substring(textBox_code.SelectionStart, 1) != " ")
                {
                    textBox_code.SelectionStart--;
                }
            }
            /*if (sender != button_find)
            {
                textBox_code.SelectionStart = textBox_code.SelectionStart + textBox_code.SelectionLength;
            }*/
            label_currentPosition.Text = textBox_code.SelectionStart.ToString() + "/" + textBox_code.TextLength.ToString();
            if (ParseEscPos.FindCommand(textBox_code.SelectionStart / 3))
            {
                ParseEscPos.FindCommandParameter();
                if (sender != button_auto)  //update interface only if it's no auto-parsing mode
                {
                    dataGridView_commands.CurrentCell = dataGridView_commands.Rows[ParseEscPos.commandDbLineNum].Cells[ParseEscPos.CSVColumns.CommandName];
                    if (ParseEscPos.itIsReply) textBox_command.Text = "[REPLY] " + ParseEscPos.commandName;
                    else textBox_command.Text = "[COMMAND] " + ParseEscPos.commandName;
                    if (ParseEscPos.crcFailed) textBox_commandDesc.Text += "!!!CRC FAILED!!! ";
                    if (ParseEscPos.lengthIncorrect) textBox_commandDesc.Text += "!!!FRAME LENGTH INCORRECT!!! ";
                    textBox_commandDesc.Text = ParseEscPos.commandDesc;
                    for (int i = 0; i < ParseEscPos.commandParamSize.Count; i++)
                    {
                        DataRow row = ResultDatabase.NewRow();
                        row[ResultColumns.Value] = ParseEscPos.commandParamValue[i];
                        row[ResultColumns.Type] = ParseEscPos.commandParamType[i];
                        row[ResultColumns.Raw] = Accessory.ConvertByteArrayToHex(ParseEscPos.commandParamRAWValue[i].ToArray());
                        row[ResultColumns.Description] = ParseEscPos.commandParamDesc[i];
                        if (ParseEscPos.commandParamType[i].ToLower() == ParseEscPos.DataTypes.Error) row[ResultColumns.Description] += ": " + GetErrorDesc(int.Parse(ParseEscPos.commandParamValue[i]));
                        ResultDatabase.Rows.Add(row);

                        if (ParseEscPos.commandParamType[i].ToLower() == ParseEscPos.DataTypes.Bitfield)  //add bitfield display
                        {
                            byte b = byte.Parse(ParseEscPos.commandParamValue[i]);
                            for (int i1 = 0; i1 < 8; i1++)
                            {
                                row = ResultDatabase.NewRow();
                                row[ResultColumns.Value] = (Accessory.GetBit(b, (byte)i1) ? (byte)1 : (byte)0).ToString();
                                row[ResultColumns.Type] = "bit" + i1.ToString();
                                row[ResultColumns.Description] = dataGridView_commands.Rows[ParseEscPos.commandParamDbLineNum[i] + i1 + 1].Cells[ParseEscPos.CSVColumns.CommandDescription].Value;
                                ResultDatabase.Rows.Add(row);
                            }
                        }
                    }
                }
                if (ParseEscPos.itIsReply && textBox_code.Text.Substring(textBox_code.SelectionStart + (ParseEscPos.commandBlockLength + 1) * 3, 3) == Accessory.ConvertByteToHex(ParseEscPos.ackSign)) textBox_code.Select(textBox_code.SelectionStart, (ParseEscPos.commandBlockLength + 2) * 3);
                else textBox_code.Select(textBox_code.SelectionStart, (ParseEscPos.commandBlockLength + 1) * 3);
            }
            else  //no command found. consider it's a string
            {
                int i = 3;
                while (!ParseEscPos.FindCommand((textBox_code.SelectionStart + i) / 3) && textBox_code.SelectionStart + i < textBox_code.TextLength) //looking for a non-parseable part end
                {
                    i += 3;
                }
                ParseEscPos.commandName = "";
                textBox_code.Select(textBox_code.SelectionStart, i);
                if (sender != button_auto)
                {
                    //textBox_command.Text += "";
                    //textBox_commandDesc.Text = "\"" + (String)textBox_code.SelectedText + "\"";
                    if (textBox_code.SelectedText == Accessory.ConvertByteToHex(ParseEscPos.ackSign)) textBox_command.Text = "ACK";
                    else if (textBox_code.SelectedText == Accessory.ConvertByteToHex(ParseEscPos.nakSign)) textBox_command.Text = "NAK";
                    else if (textBox_code.SelectedText == Accessory.ConvertByteToHex(ParseEscPos.enqSign) + Accessory.ConvertByteToHex(ParseEscPos.ackSign)) textBox_command.Text = "BUSY";
                    else textBox_command.Text = "\"" + textBox_code.SelectedText + "\"";
                    dataGridView_commands.CurrentCell = dataGridView_commands.Rows[0].Cells[0];
                    if (Accessory.PrintableHex(textBox_code.SelectedText)) textBox_commandDesc.Text = "\"" + Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage).GetString(Accessory.ConvertHexToByteArray(textBox_code.SelectedText)) + "\"";
                }
            }
            textBox_code.ScrollToCaret();
        }

        private void Button_next_Click(object sender, EventArgs e)
        {
            textBox_code.SelectionStart = textBox_code.SelectionStart + textBox_code.SelectionLength;
            //run "Find" button event as "Auto"
            Button_find_Click(button_next, EventArgs.Empty);
        }

        private void Button_auto_Click(object sender, EventArgs e)
        {
            File.WriteAllText(SourceFile + ".escpos", "");
            File.WriteAllText(SourceFile + ".list", "");
            textBox_code.Select(0, 0);
            StringBuilder asciiString = new StringBuilder();
            while (textBox_code.SelectionStart < textBox_code.TextLength)
            {
                StringBuilder saveStr = new StringBuilder();
                //run "Find" button event as "Auto"
                Button_find_Click(button_auto, EventArgs.Empty);
                if (ParseEscPos.commandName != "")
                {
                    //ParseEscPos.FindCommandParameter();  //?????????????
                    //Save ASCII string if collected till now
                    if (asciiString.Length != 0)
                    {
                        saveStr.Append("#" + ParseEscPos.commandFramePosition.ToString() + " RAW data [" + asciiString.ToString() + "]\r\n");
                        if (asciiString.ToString() == Accessory.ConvertByteToHex(ParseEscPos.ackSign)) saveStr.Append("ACK\r\n");
                        else if (asciiString.ToString() == Accessory.ConvertByteToHex(ParseEscPos.nakSign)) saveStr.Append("NAK\r\n");
                        else if (asciiString.ToString() == Accessory.ConvertByteToHex(ParseEscPos.enqSign) + Accessory.ConvertByteToHex(ParseEscPos.ackSign)) saveStr.Append("BUSY\r\n");
                        else if (Accessory.PrintableHex(asciiString.ToString())) saveStr.Append("ASCII string: \"" + Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage).GetString(Accessory.ConvertHexToByteArray(asciiString.ToString())) + "\"\r\n");
                        saveStr.Append("\r\n");
                        File.AppendAllText(SourceFile + ".list", asciiString.ToString() + "\r\n", Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage));
                        asciiString.Clear();
                    }

                    //collect command into file
                    /* RAW [12 34]
                    *  Command: "12 34" - "Description"
                    *  Printer model: "VKP80II-SX"
                    *  Parameter: "n" = "1234"[Word] - "Description"
                    *  Parameter: ...
                    */
                    saveStr.Append("#" + ParseEscPos.commandFramePosition.ToString() + " RAW data [" + textBox_code.SelectedText + "]\r\n");
                    if (ParseEscPos.itIsReply) saveStr.Append("Reply: [" + ParseEscPos.commandName + "] - \"" + ParseEscPos.commandDesc + "\"\r\n");
                    else saveStr.Append("Command: [" + ParseEscPos.commandName + "] - \"" + ParseEscPos.commandDesc + "\"\r\n");
                    for (int i = 0; i < ParseEscPos.commandParamSize.Count; i++)
                    {
                        saveStr.Append("\tParameter = ");
                        saveStr.Append("\"" + ParseEscPos.commandParamValue[i] + "\"");

                        saveStr.Append("[" + ParseEscPos.commandParamType[i] + "] - \"" + ParseEscPos.commandParamDesc[i].TrimStart('\r').TrimStart('\n').TrimEnd('\n').TrimEnd('\r').Replace("\n", "\n\t\t\t\t"));
                        if (ParseEscPos.commandParamType[i].ToLower() == ParseEscPos.DataTypes.Error) saveStr.Append(": " + GetErrorDesc(int.Parse(ParseEscPos.commandParamValue[i])));
                        saveStr.Append("\", RAW [" + Accessory.ConvertByteArrayToHex(ParseEscPos.commandParamRAWValue[i].ToArray()) + "]\r\n");

                        if (ParseEscPos.commandParamType[i].ToLower() == ParseEscPos.DataTypes.Bitfield)
                        {
                            byte b = byte.Parse(ParseEscPos.commandParamValue[i]);
                            for (int i1 = 0; i1 < 8; i1++)
                            {
                                saveStr.Append("\t\t[bit" + i1.ToString() + "]\" = \"");
                                saveStr.Append((Accessory.GetBit(b, (byte)i1) ? (byte)1 : (byte)0).ToString() + "\" - \"");
                                saveStr.Append(dataGridView_commands.Rows[ParseEscPos.commandParamDbLineNum[i] + i1 + 1].Cells[ParseEscPos.CSVColumns.CommandDescription].Value.ToString().Replace("\n", "\n\t\t\t\t"));
                                saveStr.Append("\"\r\n");
                            }
                        }
                    }
                    saveStr.Append("\r\n");
                    File.AppendAllText(SourceFile + ".list", textBox_code.SelectedText + "\r\n", Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage));
                    File.AppendAllText(SourceFile + ".escpos", saveStr.ToString(), Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage));
                }
                else  //consider this as a string and collect
                {
                    asciiString.Append(textBox_code.SelectedText);
                }
                textBox_code.SelectionStart = textBox_code.SelectionStart + textBox_code.SelectionLength;
            }
            if (asciiString.Length != 0)
            {
                StringBuilder saveStr = new StringBuilder();
                saveStr.Append("#" + ParseEscPos.commandFramePosition.ToString() + " RAW data [" + asciiString.ToString() + "]\r\n");
                if (asciiString.ToString() == Accessory.ConvertByteToHex(ParseEscPos.ackSign)) saveStr.Append("ACK");
                if (asciiString.ToString() == Accessory.ConvertByteToHex(ParseEscPos.nakSign)) saveStr.Append("NAK");
                if (asciiString.ToString() == Accessory.ConvertByteToHex(ParseEscPos.enqSign) + Accessory.ConvertByteToHex(ParseEscPos.ackSign)) saveStr.Append("BUSY");
                else if (Accessory.PrintableHex(asciiString.ToString())) saveStr.Append("ASCII string: \"" + Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage).GetString(Accessory.ConvertHexToByteArray(asciiString.ToString())) + "\"\r\n");
                saveStr.Append("\r\n");
                File.AppendAllText(SourceFile + ".list", asciiString.ToString() + "\r\n", Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage));
                File.AppendAllText(SourceFile + ".escpos", saveStr.ToString(), Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage));
                asciiString.Clear();
            }

        }

        private void TextBox_code_Leave(object sender, EventArgs e)
        {
            if (textBox_code.ReadOnly == false)
            {
                textBox_code.Text = Accessory.CheckHexString(textBox_code.Text);
                //ParseEscPos.Init(textBox_code.Text, CommandDatabase);
                ParseEscPos.sourceData.AddRange(Accessory.ConvertHexToByteArray(textBox_code.Text));
            }
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void SaveBinFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveFileDialog.FileName = SourceFile;
            saveFileDialog.Title = "Save BIN file";
            saveFileDialog.DefaultExt = "bin";
            saveFileDialog.Filter = "BIN files|*.bin|PRN files|*.prn|All files|*.*";
            saveFileDialog.ShowDialog();
        }

        private void SaveHexFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveFileDialog.FileName = SourceFile;
            saveFileDialog.Title = "Save HEX file";
            saveFileDialog.DefaultExt = "txt";
            saveFileDialog.Filter = "Text files|*.txt|HEX files|*.hex|All files|*.*";
            saveFileDialog.ShowDialog();
        }

        private void SaveCSVToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveFileDialog.FileName = PayOnlineFiscalParser.Properties.Settings.Default.CommandsDatabaseFile;
            saveFileDialog.Title = "Save CSV database";
            saveFileDialog.DefaultExt = "csv";
            saveFileDialog.Filter = "CSV files|*.csv|All files|*.*";
            saveFileDialog.ShowDialog();
        }

        private void SaveFileDialog_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
        {
            textBox_code.Text = Accessory.CheckHexString(textBox_code.Text);
            if (saveFileDialog.Title == "Save HEX file")
            {
                File.WriteAllText(saveFileDialog.FileName, textBox_code.Text, Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage));
            }
            else if (saveFileDialog.Title == "Save BIN file")
            {
                using (var stream = new FileStream(saveFileDialog.FileName, FileMode.Append))
                {
                    stream.Write(Accessory.ConvertHexToByteArray(textBox_code.Text), 0, textBox_code.TextLength / 3);
                }
            }
            else if (saveFileDialog.Title == "Save CSV database")
            {
                int columnCount = dataGridView_commands.ColumnCount;
                StringBuilder output = new StringBuilder();
                for (int i = 0; i < columnCount; i++)
                {
                    output.Append(dataGridView_commands.Columns[i].Name.ToString());
                    output.Append(";");
                }
                output.Append("\r\n");
                for (int i = 0; i < dataGridView_commands.RowCount; i++)
                {
                    for (int j = 0; j < columnCount; j++)
                    {
                        output.Append(dataGridView_commands.Rows[i].Cells[j].Value.ToString());
                        output.Append(";");
                    }
                    output.Append("\r\n");
                }
                try
                {
                    File.WriteAllText(saveFileDialog.FileName, output.ToString(), Encoding.GetEncoding(PayOnlineFiscalParser.Properties.Settings.Default.CodePage));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error writing to file " + saveFileDialog.FileName + ": " + ex.Message);
                }
            }

        }

        private void LoadBinToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog.FileName = "";
            openFileDialog.Title = "Open BIN file";
            openFileDialog.DefaultExt = "bin";
            openFileDialog.Filter = "BIN files|*.bin|PRN files|*.prn|All files|*.*";
            openFileDialog.ShowDialog();
        }

        private void LoadHexToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog.FileName = "";
            openFileDialog.Title = "Open HEX file";
            openFileDialog.DefaultExt = "txt";
            openFileDialog.Filter = "HEX files|*.hex|Text files|*.txt|All files|*.*";
            openFileDialog.ShowDialog();
        }

        private void LoadCSVToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog.FileName = "";
            openFileDialog.Title = "Open command CSV database";
            openFileDialog.DefaultExt = "csv";
            openFileDialog.Filter = "CSV files|*.csv|All files|*.*";
            openFileDialog.ShowDialog();
        }

        private void LoadErrorsCSV_toolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog.FileName = "";
            openFileDialog.Title = "Open errors CSV database";
            openFileDialog.DefaultExt = "csv";
            openFileDialog.Filter = "CSV files|*.csv|All files|*.*";
            openFileDialog.ShowDialog();
        }

        private void OpenFileDialog_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (openFileDialog.Title == "Open BIN file")  //binary data read
            {
                SourceFile = openFileDialog.FileName;
                try
                {
                    sourceData.Clear();
                    sourceData.AddRange(File.ReadAllBytes(SourceFile));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("\r\nError reading file " + SourceFile + ": " + ex.Message);
                }
                //Form1.ActiveForm.Text += " " + SourceFile;
                textBox_code.Text = Accessory.ConvertByteArrayToHex(sourceData.ToArray());
                textBox_code.Select(0, 0);
                //ParseEscPos.Init(textBox_code.Text, CommandDatabase);
                ParseEscPos.sourceData.Clear();
                ParseEscPos.sourceData.AddRange(Accessory.ConvertHexToByteArray(textBox_code.Text));
            }
            else if (openFileDialog.Title == "Open HEX file") //hex text read
            {
                SourceFile = openFileDialog.FileName;
                try
                {
                    textBox_code.Text = Accessory.CheckHexString(File.ReadAllText(SourceFile));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("\r\nError reading file " + SourceFile + ": " + ex.Message);
                }
                //Form1.ActiveForm.Text += " " + SourceFile;
                sourceData.Clear();
                sourceData.AddRange(Accessory.ConvertHexToByteArray(textBox_code.Text));
                textBox_code.Select(0, 0);
                //ParseEscPos.Init(textBox_code.Text, CommandDatabase);
                ParseEscPos.sourceData.Clear();
                ParseEscPos.sourceData.AddRange(Accessory.ConvertHexToByteArray(textBox_code.Text));
            }
            else if (openFileDialog.Title == "Open command CSV database") //hex text read
            {
                CommandDatabase = new DataTable();
                ReadCsv(openFileDialog.FileName, CommandDatabase);
                for (int i = 0; i < CommandDatabase.Rows.Count; i++) CommandDatabase.Rows[i][0] = Accessory.CheckHexString(CommandDatabase.Rows[i][0].ToString());
                dataGridView_commands.DataSource = CommandDatabase;
                ParseEscPos.commandDataBase = CommandDatabase;
            }
            else if (openFileDialog.Title == "Open errors CSV database") //hex text read
            {
                ErrorsDatabase = new DataTable();
                ReadCsv(openFileDialog.FileName, ErrorsDatabase);
            }

        }

        private void DefaultCSVToolStripTextBox_Leave(object sender, EventArgs e)
        {
            if (defaultCSVToolStripTextBox.Text != PayOnlineFiscalParser.Properties.Settings.Default.CommandsDatabaseFile)
            {
                PayOnlineFiscalParser.Properties.Settings.Default.CommandsDatabaseFile = defaultCSVToolStripTextBox.Text;
                PayOnlineFiscalParser.Properties.Settings.Default.Save();
            }
        }

        private void EnableDatabaseEditToolStripMenuItem_Click(object sender, EventArgs e)
        {
            enableDatabaseEditToolStripMenuItem.Checked = !enableDatabaseEditToolStripMenuItem.Checked;
            dataGridView_commands.ReadOnly = !enableDatabaseEditToolStripMenuItem.Checked;
        }

        private void EnableFileEditToolStripMenuItem_Click(object sender, EventArgs e)
        {
            enableFileEditToolStripMenuItem.Checked = !enableFileEditToolStripMenuItem.Checked;
            textBox_code.ReadOnly = !enableFileEditToolStripMenuItem.Checked;
        }

        private string GetErrorDesc(int errNum)
        {
            for (int i = 0; i < ErrorsDatabase.Rows.Count; i++)
            {
                if (int.Parse(ErrorsDatabase.Rows[i][0].ToString()) == errNum) return ErrorsDatabase.Rows[i][1].ToString();
            }
            return "!!!Unknown error!!!";
        }

        private void ToolStripTextBox1_Leave(object sender, EventArgs e)
        {
            if (errorsCSV_toolStripTextBox.Text != PayOnlineFiscalParser.Properties.Settings.Default.ErrorsDatabaseFile)
            {
                PayOnlineFiscalParser.Properties.Settings.Default.ErrorsDatabaseFile = errorsCSV_toolStripTextBox.Text;
                PayOnlineFiscalParser.Properties.Settings.Default.Save();
            }
        }

    }
}
