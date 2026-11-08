# Security

## How the tool handles data

- **Read-only.** The tool only sends read requests to Dataverse (`RetrieveMultiple`, `RetrieveMetadataChanges`, `RetrieveAllOptionSets`). It never creates, updates or deletes records, solutions or metadata.
- **Your connection, your permissions.** The tool uses the connection opened in XrmToolBox. Dataverse security roles apply as usual: the tool can only read what the connected user is allowed to read. It does not store, read or transmit credentials.
- **No other network access.** The tool does not call any service other than the connected Dataverse environment. There is no telemetry.
- **Local data only.** The tool stores the unique names of the selected solutions and the last export folder in the XrmToolBox settings folder of the current Windows user. Reports are written only to the location you choose in the save dialog.
- **Safe reports.** Values in HTML reports are HTML-encoded. Values in Excel reports are written as text, never as formulas.

## Reporting a vulnerability

Please do not open a public issue for security problems. Use [GitHub private vulnerability reporting](https://github.com/andrii-k-vovk01/SolutionComponentComparerTool/security/advisories/new) instead. You will get a response as soon as possible.
