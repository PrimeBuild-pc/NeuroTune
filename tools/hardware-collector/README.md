# NeuroTune hardware collector

Double-click `Collect-NeuroTune-HardwareReport.cmd`. It creates one dated JSON
file beside the collector. Send only that JSON to the NeuroTune maintainer.

The collector uses Windows' built-in PowerShell, CIM, Registry read APIs, and
CPU-set API. It does not require administrator rights, use the network, install
software, start a trace, or change settings. The source is included so anyone
can inspect it before running.

The report excludes user/computer names, serial numbers, MAC/IP addresses,
full paths, and raw PnP instance identifiers. A random report-local device key
links entries inside one JSON without enabling correlation across reports. The
existing interrupt mask value is also omitted; only its presence, Registry
type, and byte length are recorded.
