# opc-core

This is an OPC proxy, is a gateway to an OPC network that can link any other protocol to OPC.
This library is the core part of a set of modular library. It is based on the C# OPC-Foundation [link missing] 
library. It can load server node configuration from a xml file. It has a memory cache using LiteDB where the node lists is saved,
and keep the last value of the nodes variables.


# Configuration
There are two configuration files:
- One for very technical OPC related configurations, which ususally one doesn't need to change. This is called **Opc.Ua.SampleClient.Config.xml**, it is provided by the OPCFoundation software, and you can find more info on details of the config there. This file is already attached to this distribution.
- The other is a JSON file which the user must provide, here one can specify configurations for all the **connectors**.


```javascript
{
 // OPC CONFIG
endpointURL : "opc.tcp://endpoint_url",  // URL of your OPC server to connect to
reconnectPeriod : 10,               // Time interval [seconds] to wait before retry to reconnect to OPC server
publishingInterval : 1000,          // This is a subscription parameter, time interval [millisecond] at which the OPC server will send node values updates.

// LOGGER
loggerConfig :{
        loglevel : "info"  // how verbose is the logging: [debug,info,warning,error,fatal]
    },

// PERSISTANCE of nodes in the cache database 
nodesDatabase:{
    isInMemory : true,      // if to only have a memory DB or to actually store in a file, CHOOSE true, may lead to performance issue otherwise.
    filename : "filename",  // name of file in case of storing in a file.
    overwrite : false      // decide if to override an existing nodes file or to load from it.
 },


 // LOADING of nodes 
 nodesLoader : {
    filename : "nodeset.xml",           // XML file from which import the node set, Node2Set OPC specification.
    targetIdentifier : "DisplayName",   // what node property to use to identify the node, [displayName,browseName,nodeId] (case insensitive)
    browseNodes : true,   // if false use the XML file provided to load nodes, otherwise discover nodes via browsing the server (WARNING: network intensive, do not use in production)
    
    // nodes selection criterias
    whiteList : [],         // nodes will be accepted if 'targetIdentifier' match one of string in the list
    blackList : [],         // nodes will be discarder if 'targetIdentifier' match one of string in the list
    contains :  [],         // nodes will be accepted if 'targetIdentifier' contains one of the string in list
    notContain :[],         // nodes will be discarded if 'targetIdentifier' contains one of the string in list
    matchRegEx :[],         // nodes will be accepted if 'targetIdentifier' match one of the regular expression in list
 }
}
```

