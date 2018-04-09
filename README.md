# PayOnlineFiscalParser
PayKiosk Fiscal controller command parser (for RUSSIA)

Can decode commands and replies to/from PayKiosk fiscal controllers made for RUSSIA (for example PAYONLINE-01-FA).
Command and errors databases are stored into .CSV files.

Parameter types available:
 - string - printable string data;
 - number - int number;
 - money - double data formed to xxxxxxx.yy;
 - quantity - goods quantity adopted to weight definition - formed to xxxxx.yyy;
 - error# - error number for fiscal controller replies. Decoded according to errors database;
 - data - non-printable data;
 - bitfield - byte divided into bit flags;

any other types will be treated as mistakes and only RAW values displayed.
