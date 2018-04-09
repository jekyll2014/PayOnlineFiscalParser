using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

public class ParseEscPos
{
    //INTERFACES
    //source of the data to parce
    public static string sourceData = ""; //in Init()
    //source of the command description (DataTable)
    public static DataTable commandDataBase = new DataTable(); //in Init()

    //INTERNAL VARIABLES
    //Command list preselected
    //private static Dictionary<int, string> _commandList = new Dictionary<int, string>(); //in Init()

    public const string ackSign = "06 ";
    public const string nakSign = "15 ";
    public const string stxSign = "02 ";
    public const string enqSign = "05 ";
    public static bool itIsReply = false;
    public static bool crcFailed = false;
    public static bool lengthIncorrect = false;

    //RESULT VALUES
    public static int commandFrameLength;

    //place of the frame start in the text
    public static int commandFramePosition; //in findCommand()
    //Command text
    public static string commandName; //in findCommand()
    //Command desc
    public static string commandDesc; //in findCommand()
    //string number of the command found
    public static int commandDbLineNum; //in findCommand()
    //height of the command
    public static int commandDbHeight; //in findCommand()

    //string number of the command found
    public static List<int> commandParamDbLineNum = new List<int>(); //in findCommand()
    //list of command parameters real sizes
    public static List<int> commandParamSize = new List<int>(); //in findCommand()
    //list of command parameters sizes defined in the database
    public static List<string> commandParamSizeDefined = new List<string>(); //in findCommand()
    //command parameter description
    public static List<string> commandParamDesc = new List<string>(); //in findCommand()
    //command parameter type
    public static List<string> commandParamType = new List<string>(); //in findCommand()
    //command parameter RAW value
    public static List<string> commandParamRAWValue = new List<string>(); //in findCommand()
    //command parameter value
    public static List<string> commandParamValue = new List<string>(); //in findCommand()

    //Length of command+parameters text
    public static int commandBlockLength = 0;

    public class CSVColumns
    {
        public static int CommandName { get; set; } = 0;
        public static int CommandParameterSize { get; set; } = 1;
        public static int CommandParameterType { get; set; } = 2;
        public static int CommandParameterValue { get; set; } = 3;
        public static int CommandDescription { get; set; } = 4;
        public static int ReplyParameterSize { get; set; } = 5;
        public static int ReplyParameterType { get; set; } = 6;
        public static int ReplyParameterValue { get; set; } = 7;
        public static int ReplyDescription { get; set; } = 8;
    }

    public class DataTypes
    {
        public static string String { get; set; } = "string";
        public static string Number { get; set; } = "number";
        public static string Money { get; set; } = "money";
        public static string MoneySigned { get; set; } = "moneysigned";
        public static string Quantity { get; set; } = "quantity";
        public static string Error { get; set; } = "error#";
        public static string Data { get; set; } = "data";
        public static string TLVData { get; set; } = "tlvdata";
        public static string Bitfield { get; set; } = "bitfield";
    }

    internal static void Init(string _data, DataTable _dataBase)  //Setup source table of commands and source text field
    {
        sourceData = _data;
        commandDataBase = _dataBase;
    }

    public static bool FindCommand(int _pos, int lineNum = -1)
    {
        //reset all result values
        ClearCommand();

        if (sourceData.Length < _pos + 6 * 3) return false;
        //check if sequence starts with >ENQ <NAK

        //check if it's a command or reply
        if (sourceData.Substring(_pos, 9).StartsWith(enqSign + nakSign + stxSign))
        {
            CSVColumns.CommandParameterSize = 1;
            CSVColumns.CommandParameterType = 2;
            CSVColumns.CommandParameterValue = 3;
            CSVColumns.CommandDescription = 4;
            itIsReply = false;
            _pos += 9;
        }
        else if (sourceData.Substring(_pos, 6).StartsWith(ackSign + stxSign))
        {
            itIsReply = true;
            CSVColumns.CommandParameterSize = 5;
            CSVColumns.CommandParameterType = 6;
            CSVColumns.CommandParameterValue = 7;
            CSVColumns.CommandDescription = 8;
            _pos += 6;
        }
        else return false;

        //select data frame
        commandFrameLength = 0;
        commandFrameLength = Accessory.ConvertHexToByte(sourceData.Substring(_pos, 3));
        _pos += 3;

        //check if "commandFrameLength" less than "sourcedata". note the last byte of "sourcedata" is CRC.
        if (sourceData.Length - 3 < _pos + commandFrameLength * 3)
        {
            commandFrameLength = (sourceData.Length - _pos) / 3;
            lengthIncorrect = true;
        }

        //find command
        if (sourceData.Length < _pos + 3) return false; //check if it doesn't go over the last symbol
        for (int i = 0; i < commandDataBase.Rows.Count; i++)
        {
            if (commandDataBase.Rows[i][CSVColumns.CommandName].ToString() != "")
            {
                if (sourceData.Substring(_pos, 3) == commandDataBase.Rows[i][CSVColumns.CommandName].ToString() || sourceData.Substring(_pos, 6) == commandDataBase.Rows[i][CSVColumns.CommandName].ToString()) //if command matches
                {
                    if (lineNum < 0 || lineNum == i) //if string matches
                    {
                        commandName = commandDataBase.Rows[i][CSVColumns.CommandName].ToString();
                        commandDbLineNum = i;
                        commandDesc = commandDataBase.Rows[i][CSVColumns.CommandDescription].ToString();
                        commandFramePosition = _pos;
                        //get CRC of the frame
                        byte sentCRC = Accessory.ConvertHexToByte(sourceData.Substring(_pos - 1 * 3 + (commandFrameLength + 1) * 3, 3));
                        //check length of sourceData
                        byte calculatedCRC = PayOnline_CRC(Accessory.ConvertHexToByteArray(sourceData.Substring(_pos - 1 * 3, (commandFrameLength + 1) * 3)), commandFrameLength + 1);
                        if (calculatedCRC != sentCRC) crcFailed = true;
                        else crcFailed = false;
                        //check command height - how many rows are occupated
                        int i1 = 0;
                        while ((commandDbLineNum + i1 + 1) < commandDataBase.Rows.Count && commandDataBase.Rows[commandDbLineNum + i1 + 1][CSVColumns.CommandName].ToString() == "")
                        {
                            i1++;
                        }
                        commandDbHeight = i1;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public static bool FindCommandParameter()
    {
        ClearCommandParameters();
        //collect parameters
        int _stopSearch = commandDbLineNum + 1;
        while (_stopSearch < commandDataBase.Rows.Count && commandDataBase.Rows[_stopSearch][CSVColumns.CommandName].ToString() == "") _stopSearch++;
        for (int i = commandDbLineNum + 1; i < _stopSearch; i++)
        {
            if (commandDataBase.Rows[i][CSVColumns.CommandParameterSize].ToString() != "")
            {
                commandParamDbLineNum.Add(i);
                commandParamSizeDefined.Add(commandDataBase.Rows[i][CSVColumns.CommandParameterSize].ToString());
                if (commandParamSizeDefined.Last() == "?")
                {
                    commandParamSize.Add(commandFrameLength - 1);
                    for (int i1 = 0; i1 < commandParamSize.Count - 1; i1++) commandParamSize[commandParamSize.Count - 1] -= commandParamSize[i1];
                    if (commandParamSize[commandParamSize.Count - 1] < 0) commandParamSize[commandParamSize.Count - 1] = 0;
                }
                else if (commandParamSizeDefined.Last().StartsWith("@"))
                {
                    int v = 0;
                    int.TryParse(commandParamSizeDefined.Last().Substring(1), out v);
                    if (v != 0) commandParamSize.Add(commandParamSize[v - 1]);
                    else commandParamSize.Add(0);
                }
                else
                {
                    int v = 0;
                    int.TryParse(commandParamSizeDefined.Last(), out v);
                    commandParamSize.Add(v);
                }
                commandParamDesc.Add(commandDataBase.Rows[i][CSVColumns.CommandDescription].ToString());
                commandParamType.Add(commandDataBase.Rows[i][CSVColumns.CommandParameterType].ToString());
            }
        }

        //int commandParamPosition = commandFramePosition + 1 * 3;
        int commandParamPosition = commandFramePosition + commandName.Length;
        //process each parameter
        for (int parameter = 0; parameter < commandParamDbLineNum.Count; parameter++)
        {
            //collect predefined RAW values
            List<string> predefinedParamsRaw = new List<string>();
            int j = commandParamDbLineNum[parameter] + 1;
            while (j < commandDataBase.Rows.Count && commandDataBase.Rows[j][CSVColumns.CommandParameterValue].ToString() != "")
            {
                predefinedParamsRaw.Add(commandDataBase.Rows[j][CSVColumns.CommandParameterValue].ToString());
                j++;
            }

            //Calculate predefined params
            List<int> predefinedParamsVal = new List<int>();
            foreach (string formula in predefinedParamsRaw)
            {
                int val = 0;
                int.TryParse(formula.Trim(), out val);
                predefinedParamsVal.Add(val);
            }

            //get parameter from text
            bool errFlag = false;  //Error in parameter found
            string errMessage = "";

            string _prmType = commandDataBase.Rows[(int)commandParamDbLineNum[parameter]][CSVColumns.CommandParameterType].ToString().ToLower();
            if (parameter != 0) commandParamPosition = commandParamPosition + commandParamSize[parameter - 1] * 3;
            string _raw = "";
            string _val = "";

            if (_prmType == DataTypes.String)
            {
                //check if "\0" byte is limiting the string
                for (int i = 0; i < commandParamSize[parameter]; i++)
                {
                    if (sourceData.Substring(commandParamPosition + i * 3, 3) == "00 ") commandParamSize[parameter] = i + 1;
                }
                if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                {
                    _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                    _val = RawToString(_raw, commandParamSize[parameter]);
                }
                else
                {
                    errFlag = true;
                    errMessage = "!!!ERR: Out of data bound!!!";
                    if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                }
            }
            else if (_prmType == DataTypes.Number)
            {
                double l = 0;
                if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                {
                    _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                    l = RawToNumber(_raw);
                    _val = l.ToString();
                }
                else
                {
                    errFlag = true;
                    errMessage = "!!!ERR: Out of data bound!!!";
                    if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                }
            }
            else if (_prmType == DataTypes.Money || _prmType == DataTypes.MoneySigned)
            {
                double l = 0;
                if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                {
                    _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                    if (_prmType == DataTypes.Money) l = RawToMoney(_raw);
                    else l = RawToMoneySigned(_raw);
                    _val = l.ToString();
                }
                else
                {
                    errFlag = true;
                    errMessage = "!!!ERR: Out of data bound!!!";
                    if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                }
            }
            else if (_prmType == DataTypes.Quantity)
            {
                double l = 0;
                if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                {
                    _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                    l = RawToQuantity(_raw);
                    _val = l.ToString();
                }
                else
                {
                    errFlag = true;
                    errMessage = "!!!ERR: Out of data bound!!!";
                    if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                }
            }
            else if (_prmType == DataTypes.Error)
            {
                double l = 0;
                if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                {
                    _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                    l = RawToError(_raw);
                    _val = l.ToString();
                    //if error is !=0 then no more parameters returned
                    if (l != 0 && commandFrameLength == 2 && parameter == 0)
                    {
                        if (commandParamDbLineNum.Count > 1) commandParamDbLineNum.RemoveRange(1, commandParamDbLineNum.Count - parameter - 1);
                        if (commandParamSize.Count > 1) commandParamSize.RemoveRange(1, commandParamSize.Count - parameter - 1);
                        if (commandParamSizeDefined.Count > 1) commandParamSizeDefined.RemoveRange(1, commandParamSizeDefined.Count - parameter - 1);
                        if (commandParamDesc.Count > 1) commandParamDesc.RemoveRange(1, commandParamDesc.Count - parameter - 1);
                        if (commandParamType.Count > 1) commandParamType.RemoveRange(1, commandParamType.Count - parameter - 1);
                    }
                }
                else
                {
                    errFlag = true;
                    errMessage = "!!!ERR: Out of data bound!!!";
                    if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                }
            }
            else if (_prmType == DataTypes.Data)
            {
                if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                {
                    _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                    _val = RawToData(_raw);
                }
                else
                {
                    errFlag = true;
                    errMessage = "!!!ERR: Out of data bound!!!";
                    if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                }
            }
            else if (_prmType == DataTypes.TLVData)
            {
                int TlvType = 0;
                int TlvLength = 0;
                if (commandParamSize[parameter] > 0)
                {
                    if (commandParamPosition + (3 * 4) <= sourceData.Length)
                    {
                        //get type of parameter
                        TlvType = (int)RawToNumber(sourceData.Substring(commandParamPosition, 3 * 2));
                        //get gata length
                        TlvLength = (int)RawToNumber(sourceData.Substring(commandParamPosition + 3 * 2, 3 * 2));
                    }
                    //check if the size is correct
                    if (TlvLength + 4 > commandParamSize[parameter])
                    {
                        TlvLength = commandParamSize[parameter] - 4;
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                    }
                    else commandParamSize[parameter] = TlvLength + 4;

                    //get data
                    if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                    {
                        _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                        //_val = "[" + TlvType.ToString() + "]" + "[" + TlvLength.ToString() + "]" + Accessory.ConvertHexToString(_raw.Substring(12), PayOnlineFiscalParser.Properties.Settings.Default.CodePage);
                        _val = RawToTLVData(_raw, commandParamSize[parameter]);
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                    }
                }
            }
            else if (_prmType == DataTypes.Bitfield)
            {
                double l = 0;
                if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                {
                    _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                    l = RawToBitfield(_raw);
                    _val = l.ToString();
                }
                else
                {
                    errFlag = true;
                    errMessage = "!!!ERR: Out of data bound!!!";
                    if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                }
            }
            else
            {
                //flag = true;
                errFlag = true;
                errMessage = "!!!ERR: Incorrect parameter type!!!";
                if (commandParamPosition + (3 * commandParamSize[parameter]) <= sourceData.Length)
                {
                    _raw = (sourceData.Substring(commandParamPosition, 3 * commandParamSize[parameter]));
                    //_val = Accessory.ConvertHexToString(_raw, PayOnlineFiscalParser.Properties.Settings.Default.CodePage);
                }
                else
                {
                    errFlag = true;
                    errMessage = "!!!ERR: Out of data bound!!!";
                    if (commandParamPosition <= sourceData.Length) _raw = (sourceData.Substring(commandParamPosition));
                }
            }
            commandParamRAWValue.Add(_raw);
            commandParamValue.Add(_val);

            bool predefinedFound = false; //Matching predefined parameter found and it's number is in "predefinedParameterMatched"
            if (errFlag) commandParamDesc[parameter] += errMessage + "\r\n";

            //compare parameter value with predefined values to get proper description
            int predefinedParameterMatched = 0;
            for (int i1 = 0; i1 < predefinedParamsVal.Count; i1++)
            {
                if (commandParamValue[parameter] == predefinedParamsVal[i1].ToString())
                {
                    predefinedFound = true;
                    predefinedParameterMatched = i1;
                }
            }
            //commandParamDesc[parameter] += "\r\n";
            if ((commandParamDbLineNum[parameter] + predefinedParameterMatched + 1) < commandDbLineNum + commandDbHeight && predefinedFound == true)
            {
                commandParamDesc[parameter] += commandDataBase.Rows[commandParamDbLineNum[parameter] + predefinedParameterMatched + 1][CSVColumns.CommandDescription].ToString();
            }
        }
        ResultLength();
        return true;
    }

    internal static void ClearCommand()
    {
        itIsReply = false;
        crcFailed = false;
        lengthIncorrect = false;
        commandFramePosition = -1;
        commandDbLineNum = -1;
        commandDbHeight = -1;
        commandName = "";
        commandDesc = "";

        commandParamSize.Clear();
        commandParamDesc.Clear();
        commandParamType.Clear();
        commandParamValue.Clear();
        commandParamRAWValue.Clear();
        commandParamDbLineNum.Clear();
        commandBlockLength = 0;
    }

    internal static void ClearCommandParameters()
    {
        commandParamSize.Clear();
        commandParamDesc.Clear();
        commandParamType.Clear();
        commandParamValue.Clear();
        commandParamRAWValue.Clear();
        commandParamDbLineNum.Clear();
        commandBlockLength = 0;
    }

    internal static int ResultLength()  //Calc "CommandBlockLength" - length of command text in source text field
    {
        commandBlockLength = ackSign.Length + stxSign.Length + 1 * 3 + commandFrameLength * 3 + 1 * 3;
        if (!itIsReply)
        {
            commandBlockLength += enqSign.Length;
        }
        return commandBlockLength;
    }

    public static byte PayOnline_CRC(byte[] data, int length)
    {
        byte sum = data[0];
        for (int i = 1; i < length; i++)
        {
            sum ^= data[i];
        }
        return sum;
    }

    public static string RawToString(string s, int n)
    {
        string outStr = Accessory.ConvertHexToString(s, PayOnlineFiscalParser.Properties.Settings.Default.CodePage);
        if (outStr.Length > n) outStr = outStr.Substring(0, n);
        return outStr;
    }

    // !!! check TLV actual data layout
    public static string RawToTLVData(string s, int n)
    {
        if (s.Length < 12) return "";
        if (s.Length > n - 4) s = s.Substring(0, n - 4);
        string outStr = "";
        int tlvType = (int)RawToNumber(s.Substring(0, 6));
        outStr += "[" + tlvType.ToString() + "]";
        int strLength = (int)RawToNumber(s.Substring(6, 6));
        if (s.Length > 12)
        {
            if (strLength != s.Substring(12).Length / 3) outStr += "[!!!INCORRECT LENGTH]";
            outStr += Accessory.ConvertHexToString(s.Substring(12), PayOnlineFiscalParser.Properties.Settings.Default.CodePage);
        }
        return outStr;
    }

    public static double RawToNumber(string s)
    {
        double l = 0;
        byte[] b = Accessory.ConvertHexToByteArray(s);
        for (int n = 0; n < b.Length; n++)
        {
            /*if (n == b.Length - 1 && Accessory.GetBit(b[n], 7) == true)
            {
                l += (b[n] * Math.Pow(256, n));
                l = l - Math.Pow(2, b.Length * 8);
            }
            else*/
            l += (b[n] * Math.Pow(256, n));
        }
        return l;
    }

    public static double RawToMoney(string s)
    {
        double l = 0;
        byte[] b = Accessory.ConvertHexToByteArray(s);
        for (int n = 0; n < b.Length; n++)
        {
            l += (b[n] * Math.Pow(256, n));
        }
        return l / 100;
    }

    public static double RawToMoneySigned(string s)
    {
        double l = 0;
        byte[] b = Accessory.ConvertHexToByteArray(s);
        for (int n = 0; n < b.Length; n++)
        {
            if (n == b.Length - 1 && Accessory.GetBit(b[n], 7) == true)
            {
                l += (b[n] * Math.Pow(256, n));
                l = l - Math.Pow(2, b.Length * 8);
            }
            else l += (b[n] * Math.Pow(256, n));
        }
        return l / 100;
    }

    public static double RawToQuantity(string s)
    {
        double l = 0;
        byte[] b = Accessory.ConvertHexToByteArray(s);
        for (int n = 0; n < b.Length; n++)
        {
            /*if (n == b.Length - 1 && Accessory.GetBit(b[n], 7) == true)
            {
                l += (b[n] * Math.Pow(256, n));
                l = l - Math.Pow(2, b.Length * 8);
            }
            else*/
            l += (b[n] * Math.Pow(256, n));
        }
        return l / 1000;
    }

    public static double RawToError(string s)
    {
        double l = 0;
        byte[] b = Accessory.ConvertHexToByteArray(s);
        for (int n = 0; n < b.Length; n++)
        {
            l += (b[n] * Math.Pow(256, n));
        }
        return l;
    }

    public static string RawToData(string s)
    {
        return Accessory.ConvertHexToString(s, PayOnlineFiscalParser.Properties.Settings.Default.CodePage);
    }

    public static double RawToBitfield(string s)
    {
        long l = 0;
        if (s != "") long.TryParse(s.Trim(), NumberStyles.HexNumber, null, out l);
        return l;
    }

}