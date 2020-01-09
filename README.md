# OPC-Proxy Core Library

The OPC-Proxy allows to build and deploy a customized IoT gateway to connect any OPC server with your network of microservices or cloud. 
**This is the core library** of the OPC-Proxy project where all heavy lifting is performed. 
This library is suitable for monitoring and control of devices, we focused on defining a protocol for bidirectional 
communication exposing the user to a simple API, so that one can read, but also write values to the OPC server without knowing details about OPC.

**Features:**

- Suitable for monitoring and controlling devices.
- Simple API.
- Reliable OPC client build with the [OPC-foundation](https://github.com/OPCFoundation/UA-.NETStandard) standard library.
- Load nodes from an XML file (Nodeset XML spec) or simply browsing the server
- Powerful Nodes loading selection options
- Modular design with external connectors that can be added, extended and customized.
- Supported connectors: [HTTP](https://opc-proxy.readthedocs.io/en/latest/connectors.html#grpc), [Kafka](https://opc-proxy.readthedocs.io/en/latest/connectors.html#kafka), [InfluxDB](https://opc-proxy.readthedocs.io/en/latest/connectors.html#influxdb).
- Written in C#.



# Documentation

You can find all details about the library and the OPC-Proxy Project at [opc-proxy.readthedocs.io](https://opc-proxy.readthedocs.io/en/latest/intro.html).

# Use the package

Add it to your project from the [nuget repository](https://www.nuget.org/packages/opcProxy.core/)

```bash
dotnet add package opcProxy.core
```

Use it in your project:

```C#
using OpcProxyCore;

namespace myproxy
{
    class Program
    {

        static int Main(string[] args)
        {
            // instantiaing the manager,
            // this will load configuration from file or args
            serviceManager manager = new serviceManager(args);

	    // Add Connectors
	    // manager.addConnector(_my_nice_connector_);

            // This runs the OPC-Proxy manager with all core
            // functionalities: connects to server, monitor items...
            manager.run();
            return 0;
        }

    }
}
```
For more details on connectors and how to build your own custom OPC-Proxy [visit the docs](https://opc-proxy.readthedocs.io/en/latest/GettingStarted/compile.html).

