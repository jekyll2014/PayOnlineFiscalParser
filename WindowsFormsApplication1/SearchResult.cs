using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using PayOnlineFiscalParser.Properties;

namespace PayOnlineFiscalParser
{
    public class ParseEscPos
    {
        //Initial data
        //source of the data to parce
        public static List<byte> sourceData = new List<byte>(); //in Init()

        //source of the command description (DataTable)
        public static DataTable commandDataBase = new DataTable(); //in Init()

        //INTERNAL VARIABLES
        public const byte ackSign = 0x06;
        public const byte nakSign = 0x15;
        public const byte stxSign = 0x02;
        public const byte enqSign = 0x05;
        public static bool itIsReply;
        public static bool crcFailed;
        public static bool lengthIncorrect;

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
        public static List<List<byte>> commandParamRAWValue = new List<List<byte>>(); //in findCommand()

        //command parameter value
        public static List<string> commandParamValue = new List<string>(); //in findCommand()

        //Length of command+parameters text
        public static int commandBlockLength;

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
            public static string String_Null { get; set; } = "string_null";
            public static string Number { get; set; } = "number";
            public static string Money { get; set; } = "money";
            public static string MoneySigned { get; set; } = "moneysigned";
            public static string Quantity { get; set; } = "quantity";
            public static string Error { get; set; } = "error#";
            public static string Data { get; set; } = "data";
            public static string TLVData { get; set; } = "tlvdata";
            public static string Bitfield { get; set; } = "bitfield";
        }

        //lineNum = -1 - искать во всех командах
        //lineNum = x - искать в команде на определенной стоке базы
        public static bool FindCommand(int _pos, int lineNum = -1)
        {
            //reset all result values
            ClearCommand();

            if (sourceData.Count < _pos + 2) return false;
            //check if sequence starts with >ENQ <NAK

            //check if it's a command or reply
            if (sourceData[_pos] == enqSign && sourceData[_pos + 1] == nakSign && sourceData[_pos + 2] == stxSign)
            {
                CSVColumns.CommandParameterSize = 1;
                CSVColumns.CommandParameterType = 2;
                CSVColumns.CommandParameterValue = 3;
                CSVColumns.CommandDescription = 4;
                itIsReply = false;
                _pos += 3;
            }
            else if (sourceData[_pos] == ackSign && sourceData[_pos + 1] == stxSign)
            {
                itIsReply = true;
                CSVColumns.CommandParameterSize = 5;
                CSVColumns.CommandParameterType = 6;
                CSVColumns.CommandParameterValue = 7;
                CSVColumns.CommandDescription = 8;
                _pos += 2;
            }
            else
            {
                return false;
            }

            //select data frame
            commandFrameLength = sourceData[_pos];
            _pos++;

            //check if "commandFrameLength" less than "sourcedata". note the last byte of "sourcedata" is CRC.
            if (sourceData.Count - 1 < _pos + commandFrameLength)
            {
                commandFrameLength = sourceData.Count - _pos;
                lengthIncorrect = true;
            }

            //find command
            if (sourceData.Count < _pos + 1) return false; //check if it doesn't go over the last symbol
            var i = 0;
            if (lineNum != -1) i = lineNum;
            for (; i < commandDataBase.Rows.Count; i++)
                if (commandDataBase.Rows[i][CSVColumns.CommandName].ToString() != "")
                    if (sourceData[_pos] ==
                        Accessory.ConvertHexToByte(commandDataBase.Rows[i][CSVColumns.CommandName].ToString()) ||
                        Accessory.ConvertByteArrayToHex(sourceData.GetRange(_pos, 2).ToArray()) ==
                        commandDataBase.Rows[i][CSVColumns.CommandName].ToString()) //if command matches
                        if (lineNum < 0 || lineNum == i) //if string matches
                        {
                            commandName = commandDataBase.Rows[i][CSVColumns.CommandName].ToString();
                            commandDbLineNum = i;
                            commandDesc = commandDataBase.Rows[i][CSVColumns.CommandDescription].ToString();
                            commandFramePosition = _pos;
                            //get CRC of the frame
                            var sentCRC = sourceData[_pos + commandFrameLength];
                            //check length of sourceData
                            var calculatedCRC =
                                PayOnline_CRC(sourceData.GetRange(_pos - 1, commandFrameLength + 1).ToArray(),
                                    commandFrameLength + 1);
                            if (calculatedCRC != sentCRC) crcFailed = true;
                            else crcFailed = false;
                            //check command height - how many rows are occupated
                            var i1 = 0;
                            while (commandDbLineNum + i1 + 1 < commandDataBase.Rows.Count &&
                                   commandDataBase.Rows[commandDbLineNum + i1 + 1][CSVColumns.CommandName].ToString() ==
                                   "") i1++;
                            commandDbHeight = i1;
                            return true;
                        }

            return false;
        }

        public static bool FindCommandParameter()
        {
            ClearCommandParameters();
            //collect parameters
            var _stopSearch = commandDbLineNum + 1;
            while (_stopSearch < commandDataBase.Rows.Count &&
                   commandDataBase.Rows[_stopSearch][CSVColumns.CommandName].ToString() == "") _stopSearch++;
            for (var i = commandDbLineNum + 1; i < _stopSearch; i++)
                if (commandDataBase.Rows[i][CSVColumns.CommandParameterSize].ToString() != "")
                {
                    commandParamDbLineNum.Add(i);
                    commandParamSizeDefined.Add(commandDataBase.Rows[i][CSVColumns.CommandParameterSize].ToString());
                    if (commandParamSizeDefined.Last() == "?")
                    {
                        commandParamSize.Add(commandFrameLength - 1);
                        for (var i1 = 0; i1 < commandParamSize.Count - 1; i1++)
                            commandParamSize[commandParamSize.Count - 1] -= commandParamSize[i1];
                        if (commandParamSize[commandParamSize.Count - 1] < 0)
                            commandParamSize[commandParamSize.Count - 1] = 0;
                    }
                    else
                    {
                        var v = 0;
                        int.TryParse(commandParamSizeDefined.Last(), out v);
                        commandParamSize.Add(v);
                    }

                    commandParamDesc.Add(commandDataBase.Rows[i][CSVColumns.CommandDescription].ToString());
                    commandParamType.Add(commandDataBase.Rows[i][CSVColumns.CommandParameterType].ToString());
                }

            var commandParamPosition = commandFramePosition + commandName.Length / 3;
            //process each parameter
            for (var parameter = 0; parameter < commandParamDbLineNum.Count; parameter++)
            {
                //collect predefined RAW values
                var predefinedParamsRaw = new List<string>();
                var j = commandParamDbLineNum[parameter] + 1;
                while (j < commandDataBase.Rows.Count &&
                       commandDataBase.Rows[j][CSVColumns.CommandParameterValue].ToString() != "")
                {
                    predefinedParamsRaw.Add(commandDataBase.Rows[j][CSVColumns.CommandParameterValue].ToString());
                    j++;
                }

                //Calculate predefined params
                var predefinedParamsVal = new List<int>();
                foreach (var formula in predefinedParamsRaw)
                {
                    var val = 0;
                    int.TryParse(formula.Trim(), out val);
                    predefinedParamsVal.Add(val);
                }

                //substitute parameters if needed (@-sign)
                if (commandParamSizeDefined[parameter].StartsWith("%"))
                {
                    var v = -1;
                    int.TryParse(commandParamSizeDefined[parameter].Substring(1), out v);
                    if (v != -1)
                    {
                        var n = 0;
                        int.TryParse(commandParamValue[v], out n);
                        commandParamSize[parameter] = n;
                    }
                }


                //get parameter from text
                var errFlag = false; //Error in parameter found
                var errMessage = "";

                var _prmType = commandDataBase.Rows[commandParamDbLineNum[parameter]][CSVColumns.CommandParameterType]
                    .ToString().ToLower();
                if (parameter != 0) commandParamPosition = commandParamPosition + commandParamSize[parameter - 1];
                var _raw = new List<byte>();
                var _val = "";

                if (_prmType == DataTypes.String || _prmType == DataTypes.String_Null)
                {
                    //check if "\0" byte is limiting the string
                    if (_prmType == DataTypes.String_Null)
                        for (var i = 0; i < commandParamSize[parameter]; i++)
                            if (sourceData[commandParamPosition + i] == 0)
                                commandParamSize[parameter] = i + 1;
                    if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                    {
                        _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                        _val = RawToString(_raw.ToArray(), commandParamSize[parameter]);
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Count - 1)
                            _raw = sourceData.GetRange(commandParamPosition,
                                sourceData.Count - 1 - commandParamPosition);
                    }
                }
                else if (_prmType == DataTypes.Number)
                {
                    double l = 0;
                    if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                    {
                        _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                        l = RawToNumber(_raw.ToArray());
                        _val = l.ToString();
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Count - 1)
                            _raw = sourceData.GetRange(commandParamPosition,
                                sourceData.Count - 1 - commandParamPosition);
                    }
                }
                else if (_prmType == DataTypes.Money || _prmType == DataTypes.MoneySigned)
                {
                    double l = 0;
                    if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                    {
                        _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                        if (_prmType == DataTypes.Money) l = RawToMoney(_raw.ToArray());
                        else l = RawToMoneySigned(_raw.ToArray());
                        _val = l.ToString();
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Count - 1)
                            _raw = sourceData.GetRange(commandParamPosition,
                                sourceData.Count - 1 - commandParamPosition);
                    }
                }
                else if (_prmType == DataTypes.Quantity)
                {
                    double l = 0;
                    if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                    {
                        _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                        l = RawToQuantity(_raw.ToArray());
                        _val = l.ToString();
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Count - 1)
                            _raw = sourceData.GetRange(commandParamPosition,
                                sourceData.Count - 1 - commandParamPosition);
                    }
                }
                else if (_prmType == DataTypes.Error)
                {
                    double l = 0;
                    if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                    {
                        _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                        l = RawToError(_raw.ToArray());
                        _val = l.ToString();
                        //if error is !=0 then no more parameters returned
                        if (l != 0 && commandFrameLength == 2 && parameter == 0)
                        {
                            if (commandParamDbLineNum.Count > 1)
                                commandParamDbLineNum.RemoveRange(1, commandParamDbLineNum.Count - parameter - 1);
                            if (commandParamSize.Count > 1)
                                commandParamSize.RemoveRange(1, commandParamSize.Count - parameter - 1);
                            if (commandParamSizeDefined.Count > 1)
                                commandParamSizeDefined.RemoveRange(1, commandParamSizeDefined.Count - parameter - 1);
                            if (commandParamDesc.Count > 1)
                                commandParamDesc.RemoveRange(1, commandParamDesc.Count - parameter - 1);
                            if (commandParamType.Count > 1)
                                commandParamType.RemoveRange(1, commandParamType.Count - parameter - 1);
                        }
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Count - 1)
                            _raw = sourceData.GetRange(commandParamPosition,
                                sourceData.Count - 1 - commandParamPosition);
                    }
                }
                else if (_prmType == DataTypes.Data)
                {
                    if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                    {
                        _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                        _val = RawToData(_raw.ToArray());
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Count - 1)
                            _raw = sourceData.GetRange(commandParamPosition,
                                sourceData.Count - 1 - commandParamPosition);
                    }
                }
                else if (_prmType == DataTypes.TLVData)
                {
                    var TlvType = 0;
                    var TlvLength = 0;
                    if (commandParamSize[parameter] > 0)
                    {
                        if (commandParamPosition + 4 <= sourceData.Count - 1)
                        {
                            //get type of parameter
                            TlvType = (int) RawToNumber(sourceData.GetRange(commandParamPosition, 2).ToArray());
                            //get gata length
                            TlvLength = (int) RawToNumber(sourceData.GetRange(commandParamPosition + 2, 2).ToArray());
                        }

                        //check if the size is correct
                        if (TlvLength + 4 > commandParamSize[parameter])
                        {
                            TlvLength = commandParamSize[parameter] - 4;
                            errFlag = true;
                            errMessage = "!!!ERR: Out of data bound!!!";
                        }
                        else
                        {
                            commandParamSize[parameter] = TlvLength + 4;
                        }

                        //get data
                        if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                        {
                            _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                            //_val = "[" + TlvType.ToString() + "]" + "[" + TlvLength.ToString() + "]" + Accessory.ConvertHexToString(_raw.Substring(12), PayOnlineFiscalParser.Properties.Settings.Default.CodePage);
                            _val = RawToTLVData(_raw.ToArray(), commandParamSize[parameter]);
                        }
                        else
                        {
                            errFlag = true;
                            errMessage = "!!!ERR: Out of data bound!!!";
                            if (commandParamPosition <= sourceData.Count - 1)
                                _raw = sourceData.GetRange(commandParamPosition,
                                    sourceData.Count - 1 - commandParamPosition);
                        }
                    }
                }
                else if (_prmType == DataTypes.Bitfield)
                {
                    double l = 0;
                    if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                    {
                        _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                        l = RawToBitfield(_raw[0]);
                        _val = l.ToString();
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Count - 1)
                            _raw = sourceData.GetRange(commandParamPosition,
                                sourceData.Count - 1 - commandParamPosition);
                    }
                }
                else
                {
                    //flag = true;
                    errFlag = true;
                    errMessage = "!!!ERR: Incorrect parameter type!!!";
                    if (commandParamPosition + commandParamSize[parameter] <= sourceData.Count - 1)
                    {
                        _raw = sourceData.GetRange(commandParamPosition, commandParamSize[parameter]);
                        //_val = Accessory.ConvertHexToString(_raw, PayOnlineFiscalParser.Properties.Settings.Default.CodePage);
                    }
                    else
                    {
                        errFlag = true;
                        errMessage = "!!!ERR: Out of data bound!!!";
                        if (commandParamPosition <= sourceData.Count - 1)
                            _raw = sourceData.GetRange(commandParamPosition,
                                sourceData.Count - 1 - commandParamPosition);
                    }
                }

                commandParamRAWValue.Add(_raw);
                commandParamValue.Add(_val);

                var predefinedFound =
                    false; //Matching predefined parameter found and it's number is in "predefinedParameterMatched"
                if (errFlag) commandParamDesc[parameter] += errMessage + "\r\n";

                //compare parameter value with predefined values to get proper description
                var predefinedParameterMatched = 0;
                for (var i1 = 0; i1 < predefinedParamsVal.Count; i1++)
                    if (commandParamValue[parameter] == predefinedParamsVal[i1].ToString())
                    {
                        predefinedFound = true;
                        predefinedParameterMatched = i1;
                    }

                if (commandParamDbLineNum[parameter] + predefinedParameterMatched + 1 <
                    commandDbLineNum + commandDbHeight && predefinedFound)
                    commandParamDesc[parameter] +=
                        commandDataBase.Rows[commandParamDbLineNum[parameter] + predefinedParameterMatched + 1][
                            CSVColumns.CommandDescription].ToString();
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
            commandParamSizeDefined.Clear();
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
            commandParamSizeDefined.Clear();
            commandParamDesc.Clear();
            commandParamType.Clear();
            commandParamValue.Clear();
            commandParamRAWValue.Clear();
            commandParamDbLineNum.Clear();
            commandBlockLength = 0;
        }

        internal static int ResultLength() //Calc "CommandBlockLength" - length of command text in source text field
        {
            //STX + CmdLength + [CMD + Data] + CRC        
            commandBlockLength = 1 + 1 + commandFrameLength + 1;
            if (!itIsReply) commandBlockLength++;
            return commandBlockLength;
        }

        public static byte PayOnline_CRC(byte[] data, int length)
        {
            var sum = data[0];
            for (var i = 1; i < length; i++) sum ^= data[i];
            return sum;
        }

        public static string RawToString(byte[] b, int n)
        {
            var outStr = Encoding.GetEncoding(Settings.Default.CodePage).GetString(b);
            if (outStr.Length > n) outStr = outStr.Substring(0, n);
            return outStr;
        }

        // !!! check TLV actual data layout
        public static string RawToTLVData(byte[] b, int n)
        {
            var s = new List<byte>();
            s.AddRange(b);
            if (s.Count < 4) return "";
            if (s.Count > n + 4) s = s.GetRange(0, n + 4);
            var outStr = "";
            var tlvType = (int) RawToNumber(s.GetRange(0, 2).ToArray());
            outStr = "[" + tlvType + "]";
            var strLength = (int) RawToNumber(s.GetRange(2, 2).ToArray());
            outStr += "[" + strLength + "]";
            if (s.Count == 4 + strLength)
            {
                var b1 = s.GetRange(2, s.Count - 2).ToArray();
                if (Accessory.PrintableByteArray(b1))
                    outStr += "\"" + Encoding.GetEncoding(Settings.Default.CodePage)
                        .GetString(s.GetRange(4, s.Count - 4).ToArray()) + "\"";
                else outStr += "[" + Accessory.ConvertByteArrayToHex(b1) + "]";
            }
            else
            {
                outStr += "INCORRECT LENGTH";
            }

            return outStr;
        }

        public static double RawToNumber(byte[] b)
        {
            double l = 0;
            for (var n = 0; n < b.Length; n++) l += b[n] * Math.Pow(256, n);
            return l;
        }

        public static double RawToMoney(byte[] b)
        {
            double l = 0;
            for (var n = 0; n < b.Length; n++) l += b[n] * Math.Pow(256, n);
            return l / 100;
        }

        public static double RawToMoneySigned(byte[] b)
        {
            double l = 0;
            for (var n = 0; n < b.Length; n++)
                if (n == b.Length - 1 && Accessory.GetBit(b[n], 7))
                {
                    l += b[n] * Math.Pow(256, n);
                    l = l - Math.Pow(2, b.Length * 8);
                }
                else
                {
                    l += b[n] * Math.Pow(256, n);
                }

            return l / 100;
        }

        public static double RawToQuantity(byte[] b)
        {
            double l = 0;
            for (var n = 0; n < b.Length; n++) l += b[n] * Math.Pow(256, n);
            return l / 1000;
        }

        public static double RawToError(byte[] b)
        {
            double l = 0;
            for (var n = 0; n < b.Length; n++) l += b[n] * Math.Pow(256, n);
            return l;
        }

        public static string RawToData(byte[] b)
        {
            if (Accessory.PrintableByteArray(b))
                return "\"" + Encoding.GetEncoding(Settings.Default.CodePage).GetString(b) + "\"";
            return "[" + Accessory.ConvertByteArrayToHex(b) + "]";
        }

        public static double RawToBitfield(byte b)
        {
            return b;
        }
    }
}