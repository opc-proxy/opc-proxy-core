# opc-core

This is an OPC proxy, is a gateway to an OPC network that can link any other protocol to OPC.
This library is the core part of a set of modular library. It is based on the C# OPC-Foundation [link missing] 
library. It can load server node configuration from a xml file. It has a memory cache using LiteDB where the node lists is saved,
and keep the last value of the nodes variables.


# Configuration
There are two configuration files:
- One for very technical OPC related configurations, which ususally one doesn't need to change. This is called **Opc.Ua.SampleClient.Config.xml**, it is provided by the OPCFoundation software, and you can find more info on details of the config there. This file is already attached to this distribution.
- The other is a JSON file which the user must provide, here one can specify configurations for all the **connectors**.

### OPC config
```json
{
 "endpointURL":"opc.tcp://endpoint_url",  // URL of your OPC server to connect to
 "reconnectPeriod":10,               // Time interval [seconds] to wait before retry to reconnect to OPC server
 "publishingInterval": 1000          // This is a subscription parameter, time interval [millisecond] at which the OPC server will send node values updates.
}
```
### Nodes Database
```json
{
 "nodesDatabase":{
    "isInMemory":true,      // if to only have a memory DB or to actually store in a file, CHOOSE true, may lead to performance issue otherwise.
    "filename": "filename",  // name of file in case of storing in a file.
    "overwrite": false      // decide if to override an existing nodes file or to load from it.
  }
}
```
### Logger
```json
{
   "loggerConfig" :{
        "loglevel" :"info"  // how verbose is the logging: [debug,info,warning,error,fatal]
    }
}



